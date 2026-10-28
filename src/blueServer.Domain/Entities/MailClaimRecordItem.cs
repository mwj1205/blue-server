namespace blueServer.Domain.Entities;

public sealed class MailClaimRecordItem
{
    public long Id { get; private set; }
    public long MailClaimRecordId { get; private set; }
    public int ItemTemplateId { get; private set; }
    public int GrantedQuantity { get; private set; }
    public int RemainingQuantity { get; private set; }
    public int QuantityAfter { get; private set; }

    public MailClaimRecord? MailClaimRecord { get; private set; }
    public ItemTemplate? ItemTemplate { get; private set; }

    private MailClaimRecordItem()
    {
    }

    public static MailClaimRecordItem Create(MailClaimItemSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(item.ItemTemplateId);
        ArgumentOutOfRangeException.ThrowIfNegative(item.GrantedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(item.RemainingQuantity);

        if (item.QuantityAfter < item.GrantedQuantity ||
            item.QuantityAfter > ItemTemplate.InventoryMaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item), item.QuantityAfter,
                "Quantity after claim is outside the valid inventory range.");
        }

        return new MailClaimRecordItem
        {
            ItemTemplateId = item.ItemTemplateId,
            GrantedQuantity = item.GrantedQuantity,
            RemainingQuantity = item.RemainingQuantity,
            QuantityAfter = item.QuantityAfter
        };
    }
}
