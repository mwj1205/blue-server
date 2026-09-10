using blueServer.Domain.Items;

namespace blueServer.Domain.Entities;

public sealed class ItemTemplate
{
    public const int MaxNameLength = 100;
    public const int InventoryMaxQuantity = 999_999;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ItemType Type { get; private set; }
    public int MaxQuantity { get; private set; }

    private ItemTemplate()
    {
    }

    public static ItemTemplate Create(
        int id,
        string name,
        ItemType type)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "Item template id must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Item template name is required.",
                nameof(name));
        }

        var normalizedName = name.Trim();

        if (normalizedName.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Item template name must not exceed {MaxNameLength} characters.",
                nameof(name));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Item type is not supported.");
        }

        return new ItemTemplate
        {
            Id = id,
            Name = normalizedName,
            Type = type,
            MaxQuantity = InventoryMaxQuantity
        };
    }
}
