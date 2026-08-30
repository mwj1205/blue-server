using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed class MailClaimAllService
{
    public const int MaxClaimCount = RewardGrantService.MaxBatchSize;

    private readonly GameDbContext _db;
    private readonly RewardGrantService _rewardGrantService;

    public MailClaimAllService(
        GameDbContext db,
        RewardGrantService rewardGrantService)
    {
        _db = db;
        _rewardGrantService = rewardGrantService;
    }

    public async Task<MailClaimAllResult> ClaimAllAsync(
        long playerId,
        DateTime claimedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(playerId, claimedAt);

        // Mail 상태 변경과 전체 Reward 지급을 묶는 상위 Transaction 경계
        await using var transaction = await _db.Database.BeginTransactionAsync(
            cancellationToken);
        long[] selectedMailIds = [];

        try
        {
            // 오래된 Mail부터 제한된 개수만 처리하여 Transaction 크기 제한
            var candidates = await _db.Mails
                .Include(mail => mail.Attachments)
                .Where(mail =>
                    mail.PlayerId == playerId &&
                    !mail.ClaimedAt.HasValue &&
                    (!mail.ExpiresAt.HasValue ||
                        mail.ExpiresAt.Value > claimedAt) &&
                    mail.Attachments.Any())
                .OrderBy(mail => mail.SentAt)
                .ThenBy(mail => mail.Id)
                .Take(MaxClaimCount + 1)
                .ToArrayAsync(cancellationToken);

            if (candidates.Length == 0)
            {
                return await ResolveEmptyResultAsync(
                    playerId,
                    cancellationToken);
            }

            var mails = candidates
                .Take(MaxClaimCount)
                .ToArray();
            var hasMore = candidates.Length > MaxClaimCount;
            selectedMailIds = mails
                .Select(mail => mail.Id)
                .ToArray();
            var rewardRequests = mails
                .Select(MailRewardGrantRequestFactory.Create)
                .ToArray();
            var grantedGold = SumRewards(mails, RewardType.Gold);
            var grantedGem = SumRewards(mails, RewardType.Gem);

            foreach (var mail in mails)
            {
                mail.Claim(claimedAt);
            }

            // 모든 Mail의 보상과 지급 이력을 한 번의 SaveChanges로 저장
            var rewardResult = await _rewardGrantService
                .GrantBatchWithinCurrentTransactionAsync(
                    playerId,
                    rewardRequests,
                    cancellationToken);

            if (!rewardResult.IsSuccess)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();

                return MapRewardFailure(rewardResult.Status);
            }

            // 전체 지급 이력이 기존에 존재해도 Mail Claim 상태 저장
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return MailClaimAllResult.Claimed(
                mails.Length,
                grantedGold,
                grantedGem,
                rewardResult.CurrentGold,
                rewardResult.CurrentGem,
                hasMore);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            return MailClaimAllResult.ConcurrencyConflict();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            // 동시 Claim이 완료된 경우 Client의 재조회 대상으로 분류
            if (selectedMailIds.Length > 0 &&
                await HasCompletedMailAsync(
                    playerId,
                    selectedMailIds,
                    CancellationToken.None))
            {
                return MailClaimAllResult.ConcurrencyConflict();
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

    private async Task<MailClaimAllResult> ResolveEmptyResultAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
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
            ? MailClaimAllResult.PlayerNotFound()
            : MailClaimAllResult.NothingToClaim(
                balance.Gold,
                balance.Gem);
    }

    private async Task<bool> HasCompletedMailAsync(
        long playerId,
        IReadOnlyCollection<long> mailIds,
        CancellationToken cancellationToken)
    {
        return await _db.Mails
            .AsNoTracking()
            .AnyAsync(
                mail =>
                    mail.PlayerId == playerId &&
                    mailIds.Contains(mail.Id) &&
                    mail.ClaimedAt.HasValue,
                cancellationToken);
    }

    private static int SumRewards(
        IEnumerable<Mail> mails,
        RewardType rewardType)
    {
        return mails
            .SelectMany(mail => mail.Attachments)
            .Where(attachment => attachment.Type == rewardType)
            .Aggregate(
                0,
                (total, attachment) =>
                    checked(total + attachment.Amount));
    }

    private static MailClaimAllResult MapRewardFailure(
        RewardGrantBatchStatus status)
    {
        return status switch
        {
            RewardGrantBatchStatus.PlayerNotFound =>
                MailClaimAllResult.PlayerNotFound(),
            RewardGrantBatchStatus.IdempotencyConflict =>
                MailClaimAllResult.IdempotencyConflict(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unexpected reward grant batch status.")
        };
    }

    private static void ValidateRequest(
        long playerId,
        DateTime claimedAt)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (claimedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Claim time must use UTC.",
                nameof(claimedAt));
        }
    }
}

public enum MailClaimAllStatus
{
    Claimed = 0,
    NothingToClaim = 1,
    PlayerNotFound = 2,
    ConcurrencyConflict = 3,
    IdempotencyConflict = 4
}

public sealed record MailClaimAllResult(
    MailClaimAllStatus Status,
    int ClaimedMailCount,
    int GrantedGold,
    int GrantedGem,
    int CurrentGold,
    int CurrentGem,
    bool HasMore)
{
    public bool IsSuccess =>
        Status is MailClaimAllStatus.Claimed or
            MailClaimAllStatus.NothingToClaim;

    public static MailClaimAllResult Claimed(
        int claimedMailCount,
        int grantedGold,
        int grantedGem,
        int currentGold,
        int currentGem,
        bool hasMore)
    {
        return new MailClaimAllResult(
            MailClaimAllStatus.Claimed,
            claimedMailCount,
            grantedGold,
            grantedGem,
            currentGold,
            currentGem,
            hasMore);
    }

    public static MailClaimAllResult NothingToClaim(
        int currentGold,
        int currentGem)
    {
        return new MailClaimAllResult(
            MailClaimAllStatus.NothingToClaim,
            0,
            0,
            0,
            currentGold,
            currentGem,
            false);
    }

    public static MailClaimAllResult PlayerNotFound()
    {
        return Failure(MailClaimAllStatus.PlayerNotFound);
    }

    public static MailClaimAllResult ConcurrencyConflict()
    {
        return Failure(MailClaimAllStatus.ConcurrencyConflict);
    }

    public static MailClaimAllResult IdempotencyConflict()
    {
        return Failure(MailClaimAllStatus.IdempotencyConflict);
    }

    private static MailClaimAllResult Failure(
        MailClaimAllStatus status)
    {
        return new MailClaimAllResult(
            status,
            0,
            0,
            0,
            0,
            0,
            false);
    }
}
