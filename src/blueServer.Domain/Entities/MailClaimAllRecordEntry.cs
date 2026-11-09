namespace blueServer.Domain.Entities;

public sealed class MailClaimAllRecordEntry
{
    public long MailClaimAllRecordId { get; private set; }
    public int Position { get; private set; }
    public long MailClaimRecordId { get; private set; }
    public MailClaimRecord MailClaimRecord { get; private set; } = null!;

    private MailClaimAllRecordEntry()
    {
    }

    internal static MailClaimAllRecordEntry Create(int position, MailClaimRecord claim)
    {
        return new MailClaimAllRecordEntry
        {
            Position = position,
            MailClaimRecord = claim
        };
    }
}
