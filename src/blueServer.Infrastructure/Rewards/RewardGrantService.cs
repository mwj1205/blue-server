using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Rewards;

public sealed class RewardGrantService
{
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
            var result = await GrantWithinCurrentTransactionCoreAsync(
                playerId,
                requestId,
                grantRecord,
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

        // 상위 Use Case와 다른 Transaction으로 분리되는 잘못된 호출 방지
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to grant rewards within a parent operation.");
        }

        var grantRecord = RewardGrantRecord.Create(
            playerId,
            requestId,
            reason,
            DateTime.UtcNow,
            rewards);

        // Transaction의 Commit과 Rollback 책임은 Mail 등의 상위 Use Case에 귀속
        return await GrantWithinCurrentTransactionCoreAsync(
            playerId,
            requestId,
            grantRecord,
            rewards,
            cancellationToken);
    }

    private async Task<RewardGrantResult> GrantWithinCurrentTransactionCoreAsync(
        long playerId,
        Guid requestId,
        RewardGrantRecord grantRecord,
        RewardBundle rewards,
        CancellationToken cancellationToken)
    {
        var player = await _db.Players.FirstOrDefaultAsync(
            player => player.Id == playerId,
            cancellationToken);

        if (player is null)
        {
            return RewardGrantResult.PlayerNotFound();
        }

        // 동일 Transaction 내부의 기존 지급 이력 재확인
        var existingGrant = await _db.RewardGrantRecords
            .Include(record => record.Items)
            .FirstOrDefaultAsync(
                record =>
                    record.PlayerId == playerId &&
                    record.RequestId == requestId,
                cancellationToken);

        if (existingGrant is not null)
        {
            return existingGrant.HasSameGrant(grantRecord.Reason, rewards)
                ? RewardGrantResult.AlreadyGranted(
                    player.Gold,
                    player.Gem)
                : RewardGrantResult.IdempotencyConflict();
        }

        foreach (var reward in rewards.Items)
        {
            ApplyReward(player, reward);
        }

        _db.RewardGrantRecords.Add(grantRecord);
        await _db.SaveChangesAsync(cancellationToken);

        return RewardGrantResult.Granted(
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
