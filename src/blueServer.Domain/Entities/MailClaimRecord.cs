namespace blueServer.Domain.Entities;

// 수령 요청 한 번의 확정 결과. 재시도 시 현재 Mail 상태 대신 이 결과를 재사용한다.
public sealed class MailClaimRecord
{
    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public long MailId { get; private set; }
    public Guid RequestId { get; private set; }
    public DateTime ProcessedAt { get; private set; }
    public DateTime? MailClaimedAt { get; private set; }
    public int GrantedGold { get; private set; }
    public int GrantedGem { get; private set; }
    public int CurrentGold { get; private set; }
    public int CurrentGem { get; private set; }

    public Player? Player { get; private set; }
    public Mail? Mail { get; private set; }
    public ICollection<MailClaimRecordItem> Items { get; private set; } =
        new List<MailClaimRecordItem>();

    private MailClaimRecord()
    {
    }

    public static MailClaimRecord Create(
        long playerId,
        long mailId,
        Guid requestId,
        DateTime processedAt,
        DateTime? mailClaimedAt,
        int grantedGold,
        int grantedGem,
        int currentGold,
        int currentGem,
        IEnumerable<MailClaimItemSnapshot> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mailId);

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        if (processedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Processing time must use UTC.", nameof(processedAt));
        }

        if (mailClaimedAt.HasValue &&
            (mailClaimedAt.Value.Kind != DateTimeKind.Utc || mailClaimedAt.Value > processedAt))
        {
            throw new ArgumentException(
                "Mail claim time must use UTC and must not be later than processing time.",
                nameof(mailClaimedAt));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(grantedGold);
        ArgumentOutOfRangeException.ThrowIfNegative(grantedGem);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentGold, grantedGold);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentGem, grantedGem);

        var recordItems = items.Select(MailClaimRecordItem.Create).ToArray();
        if (recordItems.Select(item => item.ItemTemplateId).Distinct().Count() != recordItems.Length)
        {
            throw new ArgumentException("Claim items must have unique item template ids.", nameof(items));
        }

        if (mailClaimedAt.HasValue && recordItems.Any(item => item.RemainingQuantity > 0))
        {
            throw new ArgumentException("Completed mail must not have remaining items.", nameof(items));
        }

        var record = new MailClaimRecord
        {
            PlayerId = playerId,
            MailId = mailId,
            RequestId = requestId,
            ProcessedAt = processedAt,
            MailClaimedAt = mailClaimedAt,
            GrantedGold = grantedGold,
            GrantedGem = grantedGem,
            CurrentGold = currentGold,
            CurrentGem = currentGem
        };

        foreach (var item in recordItems)
        {
            record.Items.Add(item);
        }

        return record;
    }
}

public sealed record MailClaimItemSnapshot(
    int ItemTemplateId,
    int GrantedQuantity,
    int RemainingQuantity,
    int QuantityAfter);
