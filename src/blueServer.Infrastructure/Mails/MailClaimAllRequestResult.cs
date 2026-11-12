using blueServer.Domain.Entities;

namespace blueServer.Infrastructure.Mails;

public enum MailClaimAllRequestStatus
{
    Processed,
    NothingToClaim,
    PlayerNotFound,
    ConcurrencyConflict,
    IdempotencyConflict,
    ItemTemplateNotFound,
    ItemTemplateInactive
}

public sealed record MailClaimAllEntryResult(long MailId, MailClaimRequestResult Claim);

public sealed record MailClaimAllRequestResult(
    MailClaimAllRequestStatus Status,
    DateTime? ProcessedAt,
    int CurrentGold,
    int CurrentGem,
    bool HasMore,
    IReadOnlyList<MailClaimAllEntryResult> Entries)
{
    public int ClaimedMailCount => Entries.Count(entry => entry.Claim.Status == MailClaimRequestStatus.Claimed);
    public int GrantedGold => Entries.Sum(entry => entry.Claim.GrantedGold);
    public int GrantedGem => Entries.Sum(entry => entry.Claim.GrantedGem);

    internal static MailClaimAllRequestResult FromRecord(MailClaimAllRecord record)
    {
        var entries = record.Entries.OrderBy(entry => entry.Position)
            .Select(entry => new MailClaimAllEntryResult(
                entry.MailClaimRecord.MailId,
                MailClaimRequestResult.FromRecord(entry.MailClaimRecord)))
            .ToArray();

        return new MailClaimAllRequestResult(
            entries.Length == 0 ? MailClaimAllRequestStatus.NothingToClaim : MailClaimAllRequestStatus.Processed,
            record.ProcessedAt,
            record.CurrentGold,
            record.CurrentGem,
            record.HasMore,
            Array.AsReadOnly(entries));
    }

    internal static MailClaimAllRequestResult Failure(MailClaimAllRequestStatus status) =>
        new(status, null, 0, 0, false, Array.Empty<MailClaimAllEntryResult>());
}
