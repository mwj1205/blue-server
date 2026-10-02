namespace blueServer.Domain.Entities;

public sealed class InventoryGrantRecord
{
    public const int PayloadHashLength = 64;

    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public Guid RequestId { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTime GrantedAt { get; private set; }
    public long? MailId { get; private set; }

    public Player? Player { get; private set; }
    public Mail? Mail { get; private set; }
    public ICollection<InventoryGrantRecordItem> Items { get; private set; } =
        new List<InventoryGrantRecordItem>();

    private InventoryGrantRecord()
    {
    }

    public static InventoryGrantRecord Create(
        long playerId,
        Guid requestId,
        string payloadHash,
        DateTime grantedAt,
        long? mailId,
        IEnumerable<InventoryGrantItemSnapshot> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id must not be empty.",
                nameof(requestId));
        }

        var normalizedPayloadHash = NormalizePayloadHash(payloadHash);

        if (grantedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Inventory grant time must use UTC.",
                nameof(grantedAt));
        }

        if (mailId.HasValue && mailId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mailId),
                mailId,
                "Mail id must be greater than zero when provided.");
        }

        var itemArray = items.ToArray();

        if (itemArray.Length == 0)
        {
            throw new ArgumentException(
                "At least one inventory grant item is required.",
                nameof(items));
        }

        if (itemArray.Any(item => item is null))
        {
            throw new ArgumentException(
                "Inventory grant items must not contain null.",
                nameof(items));
        }

        if (itemArray
            .GroupBy(item => item.ItemTemplateId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Inventory grant items must have unique item template ids.",
                nameof(items));
        }

        var record = new InventoryGrantRecord
        {
            PlayerId = playerId,
            RequestId = requestId,
            PayloadHash = normalizedPayloadHash,
            GrantedAt = grantedAt,
            MailId = mailId
        };

        foreach (var item in itemArray)
        {
            record.Items.Add(InventoryGrantRecordItem.Create(item));
        }

        return record;
    }

    public bool HasSamePayload(string payloadHash)
    {
        return string.Equals(
            PayloadHash,
            NormalizePayloadHash(payloadHash),
            StringComparison.Ordinal);
    }

    private static string NormalizePayloadHash(string payloadHash)
    {
        if (string.IsNullOrWhiteSpace(payloadHash))
        {
            throw new ArgumentException(
                "Inventory grant payload hash is required.",
                nameof(payloadHash));
        }

        var normalized = payloadHash.Trim().ToLowerInvariant();

        if (normalized.Length != PayloadHashLength ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Inventory grant payload hash must be a 64-character hexadecimal SHA-256 value.",
                nameof(payloadHash));
        }

        return normalized;
    }
}

public sealed record InventoryGrantItemSnapshot(
    int ItemTemplateId,
    int RequestedQuantity,
    int AppliedQuantity,
    int OverflowQuantity,
    int QuantityAfter);
