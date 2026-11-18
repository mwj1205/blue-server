using blueServer.Domain.Items;

namespace blueServer.Infrastructure.GameData;

public sealed record GameDataCatalog
{
    public ItemCatalog ItemCatalog { get; }
    public LocalizationCatalog DefaultLocalization { get; }
    public CurrencyBoxCatalog CurrencyBoxCatalog { get; }

    private GameDataCatalog(
        ItemCatalog itemCatalog,
        LocalizationCatalog defaultLocalization,
        CurrencyBoxCatalog currencyBoxCatalog)
    {
        ItemCatalog = itemCatalog;
        DefaultLocalization = defaultLocalization;
        CurrencyBoxCatalog = currencyBoxCatalog;
    }

    public static GameDataCatalog Create(
        ItemCatalog itemCatalog,
        LocalizationCatalog defaultLocalization,
        CurrencyBoxCatalog currencyBoxCatalog)
    {
        ArgumentNullException.ThrowIfNull(itemCatalog);
        ArgumentNullException.ThrowIfNull(defaultLocalization);
        ArgumentNullException.ThrowIfNull(currencyBoxCatalog);

        foreach (var item in itemCatalog.Templates)
        {
            ValidateLocalizationKey(item.Id, item.NameKey, defaultLocalization);
            ValidateLocalizationKey(item.Id, item.DescriptionKey, defaultLocalization);
        }

        var templates = itemCatalog.Templates.ToDictionary(item => item.Id);
        foreach (var box in currencyBoxCatalog.Boxes)
        {
            if (!templates.TryGetValue(box.ItemTemplateId, out var template) ||
                template.Type != ItemType.Consumable)
            {
                throw new InvalidDataException(
                    $"Currency box {box.ItemTemplateId} must reference an existing Consumable item.");
            }
        }

        return new GameDataCatalog(itemCatalog, defaultLocalization, currencyBoxCatalog);
    }

    private static void ValidateLocalizationKey(
        int itemId,
        string key,
        LocalizationCatalog localization)
    {
        if (!localization.ContainsKey(key))
        {
            throw new InvalidDataException(
                $"Item catalog entry {itemId} references missing localization key '{key}'.");
        }
    }
}
