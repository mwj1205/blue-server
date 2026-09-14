using blueServer.Domain.Items;

namespace blueServer.Domain.Entities;

public sealed class ItemTemplate
{
    public const int MaxCodeLength = 64;
    public const int MaxLocalizationKeyLength = 150;
    public const int InventoryMaxQuantity = 999_999;

    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string NameKey { get; private set; } = string.Empty;
    public string DescriptionKey { get; private set; } = string.Empty;
    public ItemType Type { get; private set; }
    public int MaxQuantity { get; private set; }
    public bool IsActive { get; private set; }

    private ItemTemplate()
    {
    }

    public static ItemTemplate Create(
        int id,
        string code,
        string nameKey,
        string descriptionKey,
        ItemType type)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "Item template id must be greater than zero.");
        }

        var normalizedCode = NormalizeRequiredValue(
            code,
            MaxCodeLength,
            nameof(code));
        var normalizedNameKey = NormalizeRequiredValue(
            nameKey,
            MaxLocalizationKeyLength,
            nameof(nameKey));
        var normalizedDescriptionKey = NormalizeRequiredValue(
            descriptionKey,
            MaxLocalizationKeyLength,
            nameof(descriptionKey));

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
            Code = normalizedCode,
            NameKey = normalizedNameKey,
            DescriptionKey = normalizedDescriptionKey,
            Type = type,
            MaxQuantity = InventoryMaxQuantity,
            IsActive = true
        };
    }

    public bool UpdateDefinition(
        string nameKey,
        string descriptionKey,
        ItemType type)
    {
        var normalizedNameKey = NormalizeRequiredValue(
            nameKey,
            MaxLocalizationKeyLength,
            nameof(nameKey));
        var normalizedDescriptionKey = NormalizeRequiredValue(
            descriptionKey,
            MaxLocalizationKeyLength,
            nameof(descriptionKey));

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Item type is not supported.");
        }

        var changed =
            !string.Equals(NameKey, normalizedNameKey, StringComparison.Ordinal) ||
            !string.Equals(
                DescriptionKey,
                normalizedDescriptionKey,
                StringComparison.Ordinal) ||
            Type != type ||
            !IsActive;

        NameKey = normalizedNameKey;
        DescriptionKey = normalizedDescriptionKey;
        Type = type;
        IsActive = true;

        return changed;
    }

    public bool Deactivate()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        return true;
    }

    private static string NormalizeRequiredValue(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Item template value is required.",
                parameterName);
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException(
                $"Item template value must not exceed {maxLength} characters.",
                parameterName);
        }

        return normalizedValue;
    }
}
