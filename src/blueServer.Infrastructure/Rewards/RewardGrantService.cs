using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Rewards;

public sealed class RewardGrantService
{
    public const int MaxBatchSize = 100;

    private readonly GameDbContext _db;

    public RewardGrantService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<RewardGrantResult> GrantAsync(
        long playerId,
        Guid requestId,
        string reason,
        RewardBundle rewards,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rewards);

        // 재화 변경 전에 지급 식별자와 사유 검증
        var grantRecord = RewardGrantRecord.Create(
            playerId,
            requestId,
            reason,
            DateTime.UtcNow,
            rewards);

        // Transaction 시작 전 완료 이력 확인을 통한 일반 재시도 Fast Path
        var existingResult = await TryGetExistingResultAsync(
            playerId,
            requestId,
            grantRecord.Reason,
            rewards,
            cancellationToken);

        if (existingResult is not null)
        {
            return existingResult;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            var result = await GrantWithinCurrentTransactionAsync(
                playerId,
                requestId,
                grantRecord.Reason,
                rewards,
                cancellationToken);

            if (result.Status == RewardGrantStatus.Granted)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            var duplicateResult = await TryGetExistingResultAsync(
                playerId,
                requestId,
                grantRecord.Reason,
                rewards,
                CancellationToken.None);

            return duplicateResult ?? RewardGrantResult.ConcurrencyConflict();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            // Unique Constraint 경합이면 먼저 완료된 동일 요청의 결과로 복구
            var duplicateResult = await TryGetExistingResultAsync(
                playerId,
                requestId,
                grantRecord.Reason,
                rewards,
                CancellationToken.None);

            if (duplicateResult is not null)
            {
                return duplicateResult;
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<RewardGrantResult> GrantWithinCurrentTransactionAsync(
        long playerId,
        Guid requestId,
        string reason,
        RewardBundle rewards,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rewards);

        var batchResult = await GrantBatchWithinCurrentTransactionAsync(
            playerId,
            [new RewardGrantRequest(requestId, reason, rewards)],
            cancellationToken);

        return batchResult.Status switch
        {
            RewardGrantBatchStatus.Granted => RewardGrantResult.Granted(
                batchResult.CurrentGold,
                batchResult.CurrentGem),
            RewardGrantBatchStatus.AlreadyGranted =>
                RewardGrantResult.AlreadyGranted(
                    batchResult.CurrentGold,
                    batchResult.CurrentGem),
            RewardGrantBatchStatus.PlayerNotFound =>
                RewardGrantResult.PlayerNotFound(),
            RewardGrantBatchStatus.IdempotencyConflict =>
                RewardGrantResult.IdempotencyConflict(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(batchResult),
                batchResult.Status,
                "Unexpected reward grant batch status.")
        };
    }

    public async Task<RewardGrantBatchResult> GrantBatchWithinCurrentTransactionAsync(
            long playerId,
            IReadOnlyList<RewardGrantRequest> requests,
            CancellationToken cancellationToken)
    {
        ValidateBatchRequest(playerId, requests);

        // 상위 Use Case와 다른 Transaction으로 분리되는 잘못된 호출 방지
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to grant reward batches within a parent operation.");
        }

        var grantedAt = DateTime.UtcNow;
        var preparedRequests = requests
            .Select(request => new PreparedRewardGrant(
                request,
                RewardGrantRecord.Create(
                    playerId,
                    request.RequestId,
                    request.Reason,
                    grantedAt,
                    request.Rewards)))
            .ToArray();

        var player = await _db.Players.FirstOrDefaultAsync(
            player => player.Id == playerId,
            cancellationToken);

        if (player is null)
        {
            return RewardGrantBatchResult.PlayerNotFound();
        }

        var requestIds = preparedRequests
            .Select(request => request.Request.RequestId)
            .ToArray();
        var existingGrants = await _db.RewardGrantRecords
            .Include(record => record.Items)
            .Where(record =>
                record.PlayerId == playerId &&
                requestIds.Contains(record.RequestId))
            .ToDictionaryAsync(
                record => record.RequestId,
                cancellationToken);

        // 하나라도 Payload가 다르면 Batch 전체 적용 전에 중단
        foreach (var preparedRequest in preparedRequests)
        {
            if (existingGrants.TryGetValue(
                    preparedRequest.Request.RequestId,
                    out var existingGrant) &&
                !existingGrant.HasSameGrant(
                    preparedRequest.Record.Reason,
                    preparedRequest.Request.Rewards))
            {
                return RewardGrantBatchResult.IdempotencyConflict();
            }
        }

        var newRequests = preparedRequests
            .Where(request => !existingGrants.ContainsKey(
                request.Request.RequestId))
            .ToArray();

        foreach (var preparedRequest in newRequests)
        {
            foreach (var reward in preparedRequest.Request.Rewards.Items)
            {
                ApplyReward(player, reward);
            }

            _db.RewardGrantRecords.Add(preparedRequest.Record);
        }

        // Player 변경과 모든 지급 이력을 한 번의 SaveChanges로 저장
        if (newRequests.Length > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return newRequests.Length == 0
            ? RewardGrantBatchResult.AlreadyGranted(
                existingGrants.Count,
                player.Gold,
                player.Gem)
            : RewardGrantBatchResult.Granted(
                newRequests.Length,
                existingGrants.Count,
                player.Gold,
                player.Gem);
    }

    private async Task<RewardGrantResult?> TryGetExistingResultAsync(
        long playerId,
        Guid requestId,
        string reason,
        RewardBundle rewards,
        CancellationToken cancellationToken)
    {
        var existingGrant = await _db.RewardGrantRecords
            .AsNoTracking()
            .Include(record => record.Items)
            .FirstOrDefaultAsync(
                record =>
                    record.PlayerId == playerId &&
                    record.RequestId == requestId,
                cancellationToken);

        if (existingGrant is null)
        {
            return null;
        }

        if (!existingGrant.HasSameGrant(reason, rewards))
        {
            return RewardGrantResult.IdempotencyConflict();
        }

        var balance = await _db.Players
            .AsNoTracking()
            .Where(player => player.Id == playerId)
            .Select(player => new
            {
                player.Gold,
                player.Gem
            })
            .FirstOrDefaultAsync(cancellationToken);

        return balance is null
            ? RewardGrantResult.PlayerNotFound()
            : RewardGrantResult.AlreadyGranted(balance.Gold, balance.Gem);
    }

    private static void ApplyReward(Player player, RewardItem reward)
    {
        switch (reward.Type)
        {
            case RewardType.Gold:
                player.AddGold(reward.Amount);
                break;

            case RewardType.Gem:
                player.AddGems(reward.Amount);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(reward),
                    reward.Type,
                    "Reward type is not supported.");
        }
    }

    private static void ValidateBatchRequest(
        long playerId,
        IReadOnlyList<RewardGrantRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (requests.Count == 0)
        {
            throw new ArgumentException(
                "At least one reward grant request is required.",
                nameof(requests));
        }

        if (requests.Count > MaxBatchSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requests),
                requests.Count,
                $"Reward grant batch must not exceed {MaxBatchSize} requests.");
        }

        if (requests.Any(request => request is null))
        {
            throw new ArgumentException(
                "Reward grant requests must not contain null.",
                nameof(requests));
        }

        if (requests
            .GroupBy(request => request.RequestId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Reward grant request ids must be unique within a batch.",
                nameof(requests));
        }
    }

    private sealed record PreparedRewardGrant(
        RewardGrantRequest Request,
        RewardGrantRecord Record);
}

public sealed record RewardGrantRequest(
    Guid RequestId,
    string Reason,
    RewardBundle Rewards);

public enum RewardGrantBatchStatus
{
    Granted = 0,
    AlreadyGranted = 1,
    PlayerNotFound = 2,
    IdempotencyConflict = 3
}

public sealed record RewardGrantBatchResult(
    RewardGrantBatchStatus Status,
    int GrantedRequestCount,
    int AlreadyGrantedRequestCount,
    int CurrentGold,
    int CurrentGem)
{
    public bool IsSuccess =>
        Status is RewardGrantBatchStatus.Granted or
            RewardGrantBatchStatus.AlreadyGranted;

    public static RewardGrantBatchResult Granted(
        int grantedRequestCount,
        int alreadyGrantedRequestCount,
        int currentGold,
        int currentGem)
    {
        return new RewardGrantBatchResult(
            RewardGrantBatchStatus.Granted,
            grantedRequestCount,
            alreadyGrantedRequestCount,
            currentGold,
            currentGem);
    }

    public static RewardGrantBatchResult AlreadyGranted(
        int alreadyGrantedRequestCount,
        int currentGold,
        int currentGem)
    {
        return new RewardGrantBatchResult(
            RewardGrantBatchStatus.AlreadyGranted,
            0,
            alreadyGrantedRequestCount,
            currentGold,
            currentGem);
    }

    public static RewardGrantBatchResult PlayerNotFound()
    {
        return Failure(RewardGrantBatchStatus.PlayerNotFound);
    }

    public static RewardGrantBatchResult IdempotencyConflict()
    {
        return Failure(RewardGrantBatchStatus.IdempotencyConflict);
    }

    private static RewardGrantBatchResult Failure(
        RewardGrantBatchStatus status)
    {
        return new RewardGrantBatchResult(status, 0, 0, 0, 0);
    }
}

public enum RewardGrantStatus
{
    Granted,
    AlreadyGranted,
    PlayerNotFound,
    ConcurrencyConflict,
    IdempotencyConflict
}

public sealed record RewardGrantResult(
    RewardGrantStatus Status,
    int CurrentGold,
    int CurrentGem)
{
    public bool IsSuccess => Status is
        RewardGrantStatus.Granted or
        RewardGrantStatus.AlreadyGranted;

    public static RewardGrantResult Granted(int currentGold, int currentGem)
    {
        return new RewardGrantResult(
            RewardGrantStatus.Granted,
            currentGold,
            currentGem);
    }

    public static RewardGrantResult AlreadyGranted(
        int currentGold,
        int currentGem)
    {
        return new RewardGrantResult(
            RewardGrantStatus.AlreadyGranted,
            currentGold,
            currentGem);
    }

    public static RewardGrantResult PlayerNotFound()
    {
        return new RewardGrantResult(
            RewardGrantStatus.PlayerNotFound,
            0,
            0);
    }

    public static RewardGrantResult ConcurrencyConflict()
    {
        return new RewardGrantResult(
            RewardGrantStatus.ConcurrencyConflict,
            0,
            0);
    }

    public static RewardGrantResult IdempotencyConflict()
    {
        return new RewardGrantResult(
            RewardGrantStatus.IdempotencyConflict,
            0,
            0);
    }
}
