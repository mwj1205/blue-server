namespace blueServer.Domain.Rewards;

public sealed record InventoryItemReward
{
    private InventoryItemReward(int itemTemplateId, int quantity)
    {
        ItemTemplateId = itemTemplateId;
        Quantity = quantity;
    }

    public int ItemTemplateId { get; }
    public int Quantity { get; }

    public static InventoryItemReward Create(int itemTemplateId, int quantity)
    {
        if (itemTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemTemplateId),
                itemTemplateId,
                "Item template id must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Item reward quantity must be greater than zero.");
        }

        return new InventoryItemReward(itemTemplateId, quantity);
    }
}
