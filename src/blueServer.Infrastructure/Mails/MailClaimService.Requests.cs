using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.Items;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace blueServer.Infrastructure.Mails;

public sealed partial class MailClaimService
{
    // RequestId를 지원하는 새 경로. HTTP/TCP 연결 전까지 기존 호출 경로와 분리한다.
    public async Task<MailClaimRequestResult> ClaimAsync(
        long playerId,
        MailClaimRequest request,
        DateTime processedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(playerId, request.MailId, processedAt);
        processedAt = new DateTime(processedAt.Ticks - processedAt.Ticks % 10, DateTimeKind.Utc);

        var existing = await FindRequestResultAsync(playerId, request, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var mail = await _db.Mails
                .Include(mail => mail.Attachments)
                .Include(mail => mail.ItemAttachments)
                .Include(mail => mail.Player)
                .AsSplitQuery()
                .SingleOrDefaultAsync(mail => mail.Id == request.MailId && mail.PlayerId == playerId,
                    cancellationToken);

            if (mail is null)
            {
                return MailClaimRequestResult.Failure(MailClaimRequestStatus.NotFound);
            }
            if (mail.IsClaimed)
            {
                return new MailClaimRequestResult(MailClaimRequestStatus.AlreadyClaimed,
                    null, mail.ClaimedAt, 0, 0, mail.Player!.Gold, mail.Player.Gem,
                    Array.Empty<MailClaimItemSnapshot>());
            }
            if (mail.IsExpired(processedAt))
            {
                return MailClaimRequestResult.Failure(MailClaimRequestStatus.Expired);
            }
            if (processedAt < mail.SentAt)
            {
                throw new ArgumentOutOfRangeException(nameof(processedAt),
                    "Claim time must not be earlier than sent time.");
            }
            if (!mail.HasAttachments)
            {
                return MailClaimRequestResult.Failure(MailClaimRequestStatus.NoRewards);
            }

            var grantedGold = 0;
            var grantedGem = 0;
            if (mail.Attachments.Count > 0)
            {
                // 재화는 Mail 고정 키로 한 번만 지급하며 부분 수령의 RequestId와 구분
                var rewardResult = await _rewardGrantService.PrepareWithinCurrentTransactionAsync(
                    playerId, MailRewardGrantRequestFactory.Create(mail), cancellationToken);
                if (!rewardResult.IsSuccess)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                    return MailClaimRequestResult.Failure(rewardResult.Status switch
                    {
                        RewardGrantStatus.PlayerNotFound => MailClaimRequestStatus.PlayerNotFound,
                        _ => MailClaimRequestStatus.IdempotencyConflict
                    });
                }
                if (rewardResult.Status == RewardGrantStatus.Granted)
                {
                    grantedGold = mail.Attachments.Where(item => item.Type == RewardType.Gold)
                        .Sum(item => item.Amount);
                    grantedGem = mail.Attachments.Where(item => item.Type == RewardType.Gem)
                        .Sum(item => item.Amount);
                }
            }

            var items = new List<MailClaimItemSnapshot>();
            foreach (var attachment in mail.ItemAttachments
                         .Where(item => item.RemainingQuantity > 0).OrderBy(item => item.ItemTemplateId))
            {
                var increase = await _inventoryChangeService.IncreaseWithinCurrentTransactionAsync(
                    mail.Player!,
                    new InventoryIncreaseRequest(attachment.ItemTemplateId, attachment.RemainingQuantity,
                        InventoryItemChangeReasonType.MailClaim, $"mail:{mail.Id}", request.RequestId, processedAt),
                    cancellationToken);
                if (!increase.IsSuccess)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();
                    return MailClaimRequestResult.Failure(increase.Status switch
                    {
                        InventoryIncreaseStatus.ItemTemplateNotFound => MailClaimRequestStatus.ItemTemplateNotFound,
                        InventoryIncreaseStatus.ItemTemplateInactive => MailClaimRequestStatus.ItemTemplateInactive,
                        _ => throw new InvalidOperationException("Unexpected inventory increase status.")
                    });
                }

                attachment.Claim(increase.AppliedQuantity);
                items.Add(new MailClaimItemSnapshot(attachment.ItemTemplateId,
                    increase.AppliedQuantity, attachment.RemainingQuantity, increase.QuantityAfter));
            }

            if (mail.ItemAttachments.All(item => item.RemainingQuantity == 0))
            {
                mail.Claim(processedAt);
            }

            // 부분 수령에서는 값은 유지하면서 부모 Mail을 UPDATE해 xmin 경합 감지
            // 자식 첨부만 저장하면 같은 Mail의 서로 다른 요청 간 경합을 감지할 수 없음
            _db.Entry(mail).Property(candidate => candidate.ClaimedAt).IsModified = true;

            var record = MailClaimRecord.Create(playerId, mail.Id, request.RequestId,
                processedAt, mail.ClaimedAt, grantedGold, grantedGem,
                mail.Player!.Gold, mail.Player.Gem, items);
            _db.MailClaimRecords.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MailClaimRequestResult.FromRecord(record);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await FindRequestResultAsync(playerId, request, CancellationToken.None)
                ?? MailClaimRequestResult.Failure(MailClaimRequestStatus.ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (IsExpectedClaimCollision(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            return await FindRequestResultAsync(playerId, request, CancellationToken.None)
                ?? MailClaimRequestResult.Failure(MailClaimRequestStatus.ConcurrencyConflict);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<MailClaimRequestResult?> FindRequestResultAsync(
        long playerId, MailClaimRequest request, CancellationToken cancellationToken)
    {
        var record = await _db.MailClaimRecords.AsNoTracking().Include(record => record.Items)
            .SingleOrDefaultAsync(record => record.PlayerId == playerId && record.RequestId == request.RequestId,
                cancellationToken);
        return record is null ? null
            : record.MailId == request.MailId ? MailClaimRequestResult.FromRecord(record)
            : MailClaimRequestResult.Failure(MailClaimRequestStatus.IdempotencyConflict);
    }

    private static bool IsExpectedClaimCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_MailClaimRecords_PlayerId_RequestId"
                or "IX_PlayerItems_PlayerId_ItemTemplateId"
                or "IX_RewardGrantRecords_PlayerId_RequestId"
                or "IX_CurrencyChangeLogs_PlayerId_RequestId_CurrencyType"
                or "IX_InventoryItemChangeLogs_PlayerId_RequestId_ItemTemplateId"
        };
}
