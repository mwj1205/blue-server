using blueServer.Domain.Rewards;

namespace blueServer.Domain.Entities;

public sealed class MailItemAttachment
{
    public long Id { get; set; }
    public long MailId { get; set; }
    public int ItemTemplateId { get; set; }
    public int Quantity { get; set; }
    public int ClaimedQuantity { get; private set; }

    public int RemainingQuantity => Quantity - ClaimedQuantity;

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

    public void Claim(int quantity)
    {
        if (quantity < 0 || quantity > RemainingQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Claim quantity must be between zero and the remaining quantity.");
        }

        ClaimedQuantity += quantity;
    }

    // 발송 시 원본 보상 Snapshot 변환. 잔여 보상 변환과 구분한다.
    public InventoryItemReward ToInventoryItemReward()
    {
        return InventoryItemReward.Create(ItemTemplateId, Quantity);
    }
}
