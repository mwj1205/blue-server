namespace blueServer.Domain.Entities;

// 일괄 요청 한 번의 결과. 각 Mail의 지급 내역은 기존 MailClaimRecord를 참조한다.
public sealed class MailClaimAllRecord
{
    public const int MaxMailCount = 100;

    private readonly List<MailClaimAllRecordEntry> _entries = [];

    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public Guid RequestId { get; private set; }
    public DateTime ProcessedAt { get; private set; }
    public int CurrentGold { get; private set; }
    public int CurrentGem { get; private set; }
    public bool HasMore { get; private set; }

    public IReadOnlyList<MailClaimAllRecordEntry> Entries => _entries.AsReadOnly();

    private MailClaimAllRecord()
    {
    }

    public static MailClaimAllRecord Create(
        long playerId,
        Guid requestId,
        DateTime processedAt,
        int currentGold,
        int currentGem,
        bool hasMore,
        IEnumerable<MailClaimRecord> claims)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentOutOfRangeException.ThrowIfNegative(currentGold);
        ArgumentOutOfRangeException.ThrowIfNegative(currentGem);
        ArgumentNullException.ThrowIfNull(claims);

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Claim all request id must not be empty.",
                nameof(requestId));
        }

        if (processedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Processing time must use UTC.",
                nameof(processedAt));
        }

        var record = new MailClaimAllRecord
        {
            PlayerId = playerId,
            RequestId = requestId,
            ProcessedAt = processedAt,
            CurrentGold = currentGold,
            CurrentGem = currentGem,
            HasMore = hasMore
        };
        var mailIds = new HashSet<long>();
        var claimRequestIds = new HashSet<Guid> { requestId };

        foreach (var claim in claims)
        {
            if (record._entries.Count == MaxMailCount)
            {
                throw new ArgumentException(
                    $"Claim all must not exceed {MaxMailCount} Mail records.",
                    nameof(claims));
            }

            ArgumentNullException.ThrowIfNull(claim);
            if (claim.PlayerId != playerId || claim.ProcessedAt != processedAt)
            {
                throw new ArgumentException(
                    "Each claim must belong to the same player and processing time.",
                    nameof(claims));
            }

            if (!mailIds.Add(claim.MailId) || !claimRequestIds.Add(claim.RequestId))
            {
                throw new ArgumentException(
                    "Mail ids and per-Mail request ids must be unique within the batch.",
                    nameof(claims));
            }

            record._entries.Add(MailClaimAllRecordEntry.Create(record._entries.Count, claim));
        }

        return record;
    }
}
