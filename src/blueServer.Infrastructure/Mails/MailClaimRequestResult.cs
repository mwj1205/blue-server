using blueServer.Domain.Entities;

namespace blueServer.Infrastructure.Mails;

public enum MailClaimRequestStatus
{
    Claimed,
    PartiallyClaimed,
    NoCapacity,
    AlreadyClaimed,
    NotFound,
    Expired,
    NoRewards,
    PlayerNotFound,
    ConcurrencyConflict,
    IdempotencyConflict,
    ItemTemplateNotFound,
    ItemTemplateInactive
}

public sealed record MailClaimRequestResult(
    MailClaimRequestStatus Status,
    DateTime? ProcessedAt,
    DateTime? MailClaimedAt,
    int GrantedGold,
    int GrantedGem,
    int CurrentGold,
    int CurrentGem,
    IReadOnlyList<MailClaimItemSnapshot> Items)
{
    internal static MailClaimRequestResult Failure(MailClaimRequestStatus status) =>
        new(status, null, null, 0, 0, 0, 0, Array.Empty<MailClaimItemSnapshot>());

    internal static MailClaimRequestResult FromRecord(MailClaimRecord record)
    {
        var items = record.Items.OrderBy(item => item.ItemTemplateId)
            .Select(item => new MailClaimItemSnapshot(
                item.ItemTemplateId, item.GrantedQuantity, item.RemainingQuantity, item.QuantityAfter))
            .ToArray();
        var status = record.MailClaimedAt.HasValue
            ? MailClaimRequestStatus.Claimed
            : record.GrantedGold > 0 || record.GrantedGem > 0 || items.Any(item => item.GrantedQuantity > 0)
                ? MailClaimRequestStatus.PartiallyClaimed
                : MailClaimRequestStatus.NoCapacity;

        return new(status, record.ProcessedAt, record.MailClaimedAt,
            record.GrantedGold, record.GrantedGem, record.CurrentGold, record.CurrentGem,
            Array.AsReadOnly(items));
    }
}
