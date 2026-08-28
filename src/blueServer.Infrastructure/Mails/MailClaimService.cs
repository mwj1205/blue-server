using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed class MailClaimService
{
    private readonly GameDbContext _db;
    private readonly RewardGrantService _rewardGrantService;

    public MailClaimService(
        GameDbContext db,
        RewardGrantService rewardGrantService)
    {
        _db = db;
        _rewardGrantService = rewardGrantService;
    }

    public async Task<MailClaimResult> ClaimAsync(
        long playerId,
        long mailId,
        DateTime claimedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(playerId, mailId, claimedAt);

        // Mail 상태 변경과 Reward 지급을 묶는 상위 Transaction 경계
        await using var transaction = await _db.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            // 다른 Player의 Mail 존재 여부를 노출하지 않는 소유권 포함 조회
            var mail = await _db.Mails
                .Include(mail => mail.Attachments)
                .Include(mail => mail.Player)
                .FirstOrDefaultAsync(
                    mail =>
                        mail.Id == mailId &&
                        mail.PlayerId == playerId,
                    cancellationToken);

            if (mail is null)
            {
                return MailClaimResult.NotFound();
            }

            if (mail.ClaimedAt.HasValue)
            {
                return MailClaimResult.AlreadyClaimed(
                    mail.ClaimedAt.Value,
                    mail.Player!.Gold,
                    mail.Player.Gem);
            }

            if (mail.IsExpired(claimedAt))
            {
                return MailClaimResult.Expired();
            }

            if (mail.Attachments.Count == 0)
            {
                return MailClaimResult.NoRewards();
            }

            var rewardRequest = MailRewardGrantRequestFactory.Create(mail);

            // 같은 SaveChanges에 Mail 상태와 Player 재화 변경 포함
            mail.Claim(claimedAt);
            var rewardResult = await _rewardGrantService
                .GrantWithinCurrentTransactionAsync(
                    playerId,
                    rewardRequest.RequestId,
                    rewardRequest.Reason,
                    rewardRequest.Rewards,
                    cancellationToken);

            if (!rewardResult.IsSuccess)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();

                return MapRewardFailure(rewardResult.Status);
            }

            // 기존 지급 이력으로 복구된 경우에도 Mail Claim 상태 저장
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return MailClaimResult.Claimed(
                claimedAt,
                rewardResult.CurrentGold,
                rewardResult.CurrentGem);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            return await ResolveConcurrencyResultAsync(
                playerId,
                mailId,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            // 동시 요청의 Unique Constraint 경합이면 완료된 Claim 결과로 복구
            var resolvedResult = await TryResolveCompletedClaimAsync(
                playerId,
                mailId,
                cancellationToken);

            if (resolvedResult is not null)
            {
                return resolvedResult;
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

    private async Task<MailClaimResult> ResolveConcurrencyResultAsync(
        long playerId,
        long mailId,
        CancellationToken cancellationToken)
    {
        var completedResult = await TryResolveCompletedClaimAsync(
            playerId,
            mailId,
            cancellationToken);

        if (completedResult is not null)
        {
            return completedResult;
        }

        var mailExists = await _db.Mails
            .AsNoTracking()
            .AnyAsync(
                mail =>
                    mail.Id == mailId &&
                    mail.PlayerId == playerId,
                cancellationToken);

        return mailExists
            ? MailClaimResult.ConcurrencyConflict()
            : MailClaimResult.NotFound();
    }

    private async Task<MailClaimResult?> TryResolveCompletedClaimAsync(
        long playerId,
        long mailId,
        CancellationToken cancellationToken)
    {
        var currentState = await _db.Mails
            .AsNoTracking()
            .Where(mail =>
                mail.Id == mailId &&
                mail.PlayerId == playerId &&
                mail.ClaimedAt.HasValue)
            .Select(mail => new
            {
                ClaimedAt = mail.ClaimedAt!.Value,
                mail.Player!.Gold,
                mail.Player.Gem
            })
            .FirstOrDefaultAsync(cancellationToken);

        return currentState is null
            ? null
            : MailClaimResult.AlreadyClaimed(
                currentState.ClaimedAt,
                currentState.Gold,
                currentState.Gem);
    }

    private static MailClaimResult MapRewardFailure(
        RewardGrantStatus status)
    {
        return status switch
        {
            RewardGrantStatus.PlayerNotFound =>
                MailClaimResult.PlayerNotFound(),
            RewardGrantStatus.ConcurrencyConflict =>
                MailClaimResult.ConcurrencyConflict(),
            RewardGrantStatus.IdempotencyConflict =>
                MailClaimResult.IdempotencyConflict(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unexpected reward grant status.")
        };
    }

    private static void ValidateRequest(
        long playerId,
        long mailId,
        DateTime claimedAt)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (mailId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mailId),
                mailId,
                "Mail id must be greater than zero.");
        }

        if (claimedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Claim time must use UTC.",
                nameof(claimedAt));
        }
    }
}

public enum MailClaimStatus
{
    Claimed = 0,
    AlreadyClaimed = 1,
    NotFound = 2,
    Expired = 3,
    NoRewards = 4,
    PlayerNotFound = 5,
    ConcurrencyConflict = 6,
    IdempotencyConflict = 7
}

public sealed record MailClaimResult(
    MailClaimStatus Status,
    DateTime? ClaimedAt,
    int CurrentGold,
    int CurrentGem)
{
    public bool IsSuccess =>
        Status is MailClaimStatus.Claimed or
            MailClaimStatus.AlreadyClaimed;

    public static MailClaimResult Claimed(
        DateTime claimedAt,
        int currentGold,
        int currentGem)
    {
        return new MailClaimResult(
            MailClaimStatus.Claimed,
            claimedAt,
            currentGold,
            currentGem);
    }

    public static MailClaimResult AlreadyClaimed(
        DateTime claimedAt,
        int currentGold,
        int currentGem)
    {
        return new MailClaimResult(
            MailClaimStatus.AlreadyClaimed,
            claimedAt,
            currentGold,
            currentGem);
    }

    public static MailClaimResult NotFound()
    {
        return Failure(MailClaimStatus.NotFound);
    }

    public static MailClaimResult Expired()
    {
        return Failure(MailClaimStatus.Expired);
    }

    public static MailClaimResult NoRewards()
    {
        return Failure(MailClaimStatus.NoRewards);
    }

    public static MailClaimResult PlayerNotFound()
    {
        return Failure(MailClaimStatus.PlayerNotFound);
    }

    public static MailClaimResult ConcurrencyConflict()
    {
        return Failure(MailClaimStatus.ConcurrencyConflict);
    }

    public static MailClaimResult IdempotencyConflict()
    {
        return Failure(MailClaimStatus.IdempotencyConflict);
    }

    private static MailClaimResult Failure(MailClaimStatus status)
    {
        return new MailClaimResult(status, null, 0, 0);
    }
}
