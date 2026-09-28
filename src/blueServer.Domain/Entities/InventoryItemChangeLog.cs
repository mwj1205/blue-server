using blueServer.Domain.Items;

namespace blueServer.Domain.Entities;

public sealed class InventoryItemChangeLog
{
    public const int MaxSourceIdLength = 200;

    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public int ItemTemplateId { get; private set; }
    public int Delta { get; private set; }
    public int QuantityBefore { get; private set; }
    public int QuantityAfter { get; private set; }
    public InventoryItemChangeReasonType ReasonType { get; private set; }
    public string SourceId { get; private set; } = string.Empty;
    public Guid RequestId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Player? Player { get; private set; }
    public ItemTemplate? ItemTemplate { get; private set; }

    private InventoryItemChangeLog()
    {
    }

    public static InventoryItemChangeLog Create(
        long playerId,
        int itemTemplateId,
        int delta,
        int quantityBefore,
        InventoryItemChangeReasonType reasonType,
        string sourceId,
        Guid requestId,
        DateTime createdAt)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (itemTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemTemplateId),
                itemTemplateId,
                "Item template id must be greater than zero.");
        }

        if (delta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "Inventory item delta must not be zero.");
        }

        if (quantityBefore < 0 ||
            quantityBefore > ItemTemplate.InventoryMaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityBefore),
                quantityBefore,
                "Inventory item quantity must be within the supported range.");
        }

        if (!Enum.IsDefined(reasonType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasonType),
                reasonType,
                "Inventory item change reason type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException(
                "Inventory item change source id is required.",
                nameof(sourceId));
        }

        var normalizedSourceId = sourceId.Trim();

        if (normalizedSourceId.Length > MaxSourceIdLength)
        {
            throw new ArgumentException(
                $"Inventory item change source id must not exceed {MaxSourceIdLength} characters.",
                nameof(sourceId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id must not be empty.",
                nameof(requestId));
        }

        if (createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Inventory item change time must use UTC.",
                nameof(createdAt));
        }

        var quantityAfter = checked(quantityBefore + delta);

        if (quantityAfter < 0 ||
            quantityAfter > ItemTemplate.InventoryMaxQuantity)
        {
            throw new InvalidOperationException(
                "Inventory item quantity must remain within the supported range.");
        }

        return new InventoryItemChangeLog
        {
            PlayerId = playerId,
            ItemTemplateId = itemTemplateId,
            Delta = delta,
            QuantityBefore = quantityBefore,
            QuantityAfter = quantityAfter,
            ReasonType = reasonType,
            SourceId = normalizedSourceId,
            RequestId = requestId,
            CreatedAt = createdAt
        };
    }
}
