using blueServer.Domain.Entities;
using blueServer.Domain.Items;

namespace blueServer.Infrastructure.Items;

public sealed record InventoryDecreaseItem(int ItemTemplateId, int Amount);

public sealed class InventoryDecreaseRequest
{
    public const int MaxItemCount = 100;

    public IReadOnlyList<InventoryDecreaseItem> Items { get; }
    public InventoryItemChangeReasonType ReasonType { get; }
    public string SourceId { get; }
    public Guid RequestId { get; }
    public DateTime ChangedAt { get; }

    public InventoryDecreaseRequest(
        IEnumerable<InventoryDecreaseItem> items,
        InventoryItemChangeReasonType reasonType,
        string sourceId,
        Guid requestId,
        DateTime changedAt)
    {
        ArgumentNullException.ThrowIfNull(items);
        var itemArray = items.Take(MaxItemCount + 1).ToArray();
        if (itemArray.Length is < 1 or > MaxItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(items), "Between 1 and 100 costs are required.");
        }

        foreach (var item in itemArray)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.ItemTemplateId <= 0 || item.Amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Item ids and amounts must be positive.");
            }
        }

        if (!Enum.IsDefined(reasonType))
        {
            throw new ArgumentOutOfRangeException(nameof(reasonType));
        }

        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Trim().Length > InventoryItemChangeLog.MaxSourceIdLength)
        {
            throw new ArgumentException("A valid inventory change source id is required.", nameof(sourceId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        if (changedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Inventory change time must use UTC.", nameof(changedAt));
        }

        Items = Array.AsReadOnly(itemArray.GroupBy(item => item.ItemTemplateId)
            .OrderBy(group => group.Key)
            .Select(group => new InventoryDecreaseItem(group.Key, group.Sum(item => item.Amount)))
            .ToArray());
        ReasonType = reasonType;
        SourceId = sourceId.Trim();
        RequestId = requestId;
        ChangedAt = changedAt;
    }
}

public enum InventoryDecreaseStatus
{
    Decreased,
    InsufficientQuantity,
    ItemTemplateNotFound,
    ItemTemplateInactive
}

public sealed record InventoryDecreaseItemResult(int ItemTemplateId, int ConsumedQuantity, int QuantityAfter);

public sealed record InventoryDecreaseResult(
    InventoryDecreaseStatus Status,
    int? FailedItemTemplateId,
    int RequestedQuantity,
    int AvailableQuantity,
    IReadOnlyList<InventoryDecreaseItemResult> Items)
{
    public bool IsSuccess => Status == InventoryDecreaseStatus.Decreased;
}
