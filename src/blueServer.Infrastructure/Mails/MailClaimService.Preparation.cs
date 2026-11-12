using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.Items;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Mails;

public sealed partial class MailClaimService
{
    // 같은 DbContext의 상위 작업이 Transaction을 소유한다.
    // 이 메서드는 저장하지 않으며, 실패 결과를 받으면 호출자가 전체 작업을 Rollback해야 한다.
    // RequestId의 재요청 확인과 충돌 복구도 상위 작업에서 수행한다.
    // 반환 결과는 아직 확정되지 않았으며 상위 Commit 성공 후에만 Client로 전달한다.
    internal async Task<PreparedMailClaim> PrepareWithinCurrentTransactionAsync(
        long playerId,
        MailClaimRequest request,
        DateTime processedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(playerId, request.MailId, processedAt);

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to prepare a Mail claim.");
        }

        var mail = await _db.Mails
            .Include(mail => mail.Attachments)
            .Include(mail => mail.ItemAttachments)
            .Include(mail => mail.Player)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                mail => mail.Id == request.MailId && mail.PlayerId == playerId,
                cancellationToken);

        if (mail is null)
        {
            return new(MailClaimRequestResult.Failure(MailClaimRequestStatus.NotFound));
        }

        return await PrepareTrackedMailAsync(mail, request.RequestId, processedAt, cancellationToken);
    }

    internal async Task<PreparedMailClaim> PrepareTrackedMailAsync(
        Mail mail,
        Guid requestId,
        DateTime processedAt,
        CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is null ||
            _db.Entry(mail).State == EntityState.Detached ||
            mail.Player is null)
        {
            throw new InvalidOperationException(
                "Mail and its Player must be tracked in the active claim transaction.");
        }

        var player = mail.Player;

        if (mail.IsClaimed)
        {
            return new(new MailClaimRequestResult(
                MailClaimRequestStatus.AlreadyClaimed,
                null,
                mail.ClaimedAt,
                0,
                0,
                player.Gold,
                player.Gem,
                Array.Empty<MailClaimItemSnapshot>()));
        }

        if (mail.IsExpired(processedAt))
        {
            return new(MailClaimRequestResult.Failure(MailClaimRequestStatus.Expired));
        }

        if (processedAt < mail.SentAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processedAt),
                "Claim time must not be earlier than sent time.");
        }

        if (!mail.HasAttachments)
        {
            return new(MailClaimRequestResult.Failure(MailClaimRequestStatus.NoRewards));
        }

        var grantedGold = 0;
        var grantedGem = 0;

        if (mail.Attachments.Count > 0)
        {
            // 재화는 Mail 고정 키로 한 번만 지급하며 부분 수령의 RequestId와 구분
            var rewardResult = await _rewardGrantService.PrepareWithinCurrentTransactionAsync(
                mail.PlayerId,
                MailRewardGrantRequestFactory.Create(mail),
                cancellationToken);

            if (!rewardResult.IsSuccess)
            {
                return new(MailClaimRequestResult.Failure(rewardResult.Status switch
                {
                    RewardGrantStatus.PlayerNotFound => MailClaimRequestStatus.PlayerNotFound,
                    _ => MailClaimRequestStatus.IdempotencyConflict
                }));
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
                     .Where(item => item.RemainingQuantity > 0)
                     .OrderBy(item => item.ItemTemplateId))
        {
            var increase = await _inventoryChangeService.IncreaseWithinCurrentTransactionAsync(
                player,
                new InventoryIncreaseRequest(
                    attachment.ItemTemplateId,
                    attachment.RemainingQuantity,
                    InventoryItemChangeReasonType.MailClaim,
                    $"mail:{mail.Id}",
                    requestId,
                    processedAt),
                cancellationToken);

            if (!increase.IsSuccess)
            {
                return new(MailClaimRequestResult.Failure(increase.Status switch
                {
                    InventoryIncreaseStatus.ItemTemplateNotFound => MailClaimRequestStatus.ItemTemplateNotFound,
                    InventoryIncreaseStatus.ItemTemplateInactive => MailClaimRequestStatus.ItemTemplateInactive,
                    _ => throw new InvalidOperationException(
                        "Unexpected inventory increase status.")
                }));
            }

            attachment.Claim(increase.AppliedQuantity);
            items.Add(new MailClaimItemSnapshot(
                attachment.ItemTemplateId,
                increase.AppliedQuantity,
                attachment.RemainingQuantity,
                increase.QuantityAfter));
        }

        if (mail.ItemAttachments.All(item => item.RemainingQuantity == 0))
        {
            mail.Claim(processedAt);
        }

        // 부분 수령에서는 값은 유지하면서 부모 Mail을 UPDATE해 xmin 경합 감지
        // 자식 첨부만 저장하면 같은 Mail의 서로 다른 요청 간 경합을 감지할 수 없음
        _db.Entry(mail).Property(candidate => candidate.ClaimedAt).IsModified = true;

        var record = MailClaimRecord.Create(
            mail.PlayerId,
            mail.Id,
            requestId,
            processedAt,
            mail.ClaimedAt,
            grantedGold,
            grantedGem,
            player.Gold,
            player.Gem,
            items);
        _db.MailClaimRecords.Add(record);

        return new(MailClaimRequestResult.FromRecord(record), record);
    }
}

internal sealed record PreparedMailClaim(
    MailClaimRequestResult Result,
    MailClaimRecord? Record = null);
