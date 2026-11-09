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
    internal async Task<MailClaimRequestResult> PrepareWithinCurrentTransactionAsync(
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
            return MailClaimRequestResult.Failure(MailClaimRequestStatus.NotFound);
        }

        if (mail.IsClaimed)
        {
            return new MailClaimRequestResult(
                MailClaimRequestStatus.AlreadyClaimed,
                null,
                mail.ClaimedAt,
                0,
                0,
                mail.Player!.Gold,
                mail.Player.Gem,
                Array.Empty<MailClaimItemSnapshot>());
        }

        if (mail.IsExpired(processedAt))
        {
            return MailClaimRequestResult.Failure(MailClaimRequestStatus.Expired);
        }

        if (processedAt < mail.SentAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processedAt),
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
                playerId,
                MailRewardGrantRequestFactory.Create(mail),
                cancellationToken);

            if (!rewardResult.IsSuccess)
            {
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
                     .Where(item => item.RemainingQuantity > 0)
                     .OrderBy(item => item.ItemTemplateId))
        {
            var increase = await _inventoryChangeService.IncreaseWithinCurrentTransactionAsync(
                mail.Player!,
                new InventoryIncreaseRequest(
                    attachment.ItemTemplateId,
                    attachment.RemainingQuantity,
                    InventoryItemChangeReasonType.MailClaim,
                    $"mail:{mail.Id}",
                    request.RequestId,
                    processedAt),
                cancellationToken);

            if (!increase.IsSuccess)
            {
                return MailClaimRequestResult.Failure(increase.Status switch
                {
                    InventoryIncreaseStatus.ItemTemplateNotFound => MailClaimRequestStatus.ItemTemplateNotFound,
                    InventoryIncreaseStatus.ItemTemplateInactive => MailClaimRequestStatus.ItemTemplateInactive,
                    _ => throw new InvalidOperationException(
                        "Unexpected inventory increase status.")
                });
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
            playerId,
            mail.Id,
            request.RequestId,
            processedAt,
            mail.ClaimedAt,
            grantedGold,
            grantedGem,
            mail.Player!.Gold,
            mail.Player.Gem,
            items);
        _db.MailClaimRecords.Add(record);

        return MailClaimRequestResult.FromRecord(record);
    }
}
