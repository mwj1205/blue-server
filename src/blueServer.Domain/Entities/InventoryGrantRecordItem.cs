namespace blueServer.Domain.Entities;

public sealed class InventoryGrantRecordItem
{
    public long Id { get; private set; }
    public long InventoryGrantRecordId { get; private set; }
    public int ItemTemplateId { get; private set; }
    public int RequestedQuantity { get; private set; }
    public int AppliedQuantity { get; private set; }
    public int OverflowQuantity { get; private set; }
    public int QuantityAfter { get; private set; }

    public InventoryGrantRecord? InventoryGrantRecord { get; private set; }
    public ItemTemplate? ItemTemplate { get; private set; }

    private InventoryGrantRecordItem()
    {
    }

    public static InventoryGrantRecordItem Create(
        InventoryGrantItemSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.ItemTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                item.ItemTemplateId,
                "Item template id must be greater than zero.");
        }

        if (item.RequestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                item.RequestedQuantity,
                "Requested quantity must be greater than zero.");
        }

        if (item.AppliedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                item.AppliedQuantity,
                "Applied quantity must not be negative.");
        }

        if (item.OverflowQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                item.OverflowQuantity,
                "Overflow quantity must not be negative.");
        }

        if (item.RequestedQuantity !=
            checked(item.AppliedQuantity + item.OverflowQuantity))
        {
            throw new ArgumentException(
                "Requested quantity must equal applied plus overflow quantity.",
                nameof(item));
        }

        if (item.QuantityAfter < item.AppliedQuantity ||
            item.QuantityAfter > ItemTemplate.InventoryMaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                item.QuantityAfter,
                "Quantity after grant is outside the valid inventory range.");
        }

        return new InventoryGrantRecordItem
        {
            ItemTemplateId = item.ItemTemplateId,
            RequestedQuantity = item.RequestedQuantity,
            AppliedQuantity = item.AppliedQuantity,
            OverflowQuantity = item.OverflowQuantity,
            QuantityAfter = item.QuantityAfter
        };
    }
}
