using blueServer.Domain.Rewards;

namespace blueServer.Domain.Entities;

public sealed class MailItemAttachment
{
    public long Id { get; set; }
    public long MailId { get; set; }
    public int ItemTemplateId { get; set; }
    public int Quantity { get; set; }

    public Mail? Mail { get; set; }
    public ItemTemplate? ItemTemplate { get; set; }

    public static MailItemAttachment Create(
        int itemTemplateId,
        int quantity)
    {
        var reward = InventoryItemReward.Create(itemTemplateId, quantity);

        return new MailItemAttachment
        {
            ItemTemplateId = reward.ItemTemplateId,
            Quantity = reward.Quantity
        };
    }

    public InventoryItemReward ToInventoryItemReward()
    {
        return InventoryItemReward.Create(ItemTemplateId, Quantity);
    }
}
