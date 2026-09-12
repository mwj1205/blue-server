namespace blueServer.Infrastructure.GameData;

public sealed record GameDataCatalog
{
    public ItemCatalog ItemCatalog { get; }
    public LocalizationCatalog DefaultLocalization { get; }

    private GameDataCatalog(
        ItemCatalog itemCatalog,
        LocalizationCatalog defaultLocalization)
    {
        ItemCatalog = itemCatalog;
        DefaultLocalization = defaultLocalization;
    }

    public static GameDataCatalog Create(
        ItemCatalog itemCatalog,
        LocalizationCatalog defaultLocalization)
    {
        ArgumentNullException.ThrowIfNull(itemCatalog);
        ArgumentNullException.ThrowIfNull(defaultLocalization);

        foreach (var item in itemCatalog.Templates)
        {
            ValidateLocalizationKey(item.Id, item.NameKey, defaultLocalization);
            ValidateLocalizationKey(item.Id, item.DescriptionKey, defaultLocalization);
        }

        return new GameDataCatalog(itemCatalog, defaultLocalization);
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
