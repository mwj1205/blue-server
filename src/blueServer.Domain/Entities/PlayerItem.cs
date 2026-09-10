using blueServer.Domain.Items;

namespace blueServer.Domain.Entities;

public sealed class PlayerItem
{
    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public int ItemTemplateId { get; private set; }
    public int Quantity { get; private set; }
    public uint Version { get; private set; }

    public Player? Player { get; private set; }
    public ItemTemplate? ItemTemplate { get; private set; }

    private PlayerItem()
    {
    }

    public static PlayerItem Create(
        long playerId,
        ItemTemplate itemTemplate,
        int quantity)
    {
        ArgumentNullException.ThrowIfNull(itemTemplate);

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Item quantity must be greater than zero.");
        }

        if (quantity > itemTemplate.MaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "Item quantity must not exceed the maximum quantity.");
        }

        return new PlayerItem
        {
            PlayerId = playerId,
            ItemTemplateId = itemTemplate.Id,
            Quantity = quantity,
            ItemTemplate = itemTemplate
        };
    }

    public ItemIncreaseResult IncreaseUpToLimit(int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        if (ItemTemplate is null)
        {
            throw new InvalidOperationException(
                "Item template must be loaded before changing quantity.");
        }

        var availableQuantity = ItemTemplate.MaxQuantity - Quantity;
        var appliedQuantity = Math.Min(amount, availableQuantity);
        var overflowQuantity = amount - appliedQuantity;

        Quantity += appliedQuantity;

        return new ItemIncreaseResult(
            RequestedQuantity: amount,
            AppliedQuantity: appliedQuantity,
            OverflowQuantity: overflowQuantity,
            QuantityAfter: Quantity);
    }

    public bool TryDecrease(int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Amount must be greater than zero.");
        }

        if (Quantity < amount)
        {
            return false;
        }

        Quantity -= amount;
        return true;
    }
}
