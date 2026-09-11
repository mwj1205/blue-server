namespace blueServer.Infrastructure.GameData;

public sealed class EmbeddedGameDataLoader
{
    private const string ItemResourceName =
        "blueServer.GameData.Items.item-templates.v1.json";
    private const string DefaultLocalizationResourceName =
        "blueServer.GameData.Localization.ko-KR.v1.json";

    public async Task<GameDataCatalog> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var assembly = typeof(EmbeddedGameDataLoader).Assembly;

        await using var itemStream = assembly.GetManifestResourceStream(ItemResourceName) ??
            throw CreateMissingResourceException(ItemResourceName);
        await using var localizationStream = assembly.GetManifestResourceStream(
            DefaultLocalizationResourceName) ??
            throw CreateMissingResourceException(DefaultLocalizationResourceName);

        var itemCatalog = await ItemCatalogJsonLoader.LoadAsync(
            itemStream,
            cancellationToken);
        var localization = await LocalizationCatalogJsonLoader.LoadAsync(
            localizationStream,
            cancellationToken);

        return GameDataCatalog.Create(itemCatalog, localization);
    }

    private static InvalidOperationException CreateMissingResourceException(
        string resourceName)
    {
        return new InvalidOperationException(
            $"Embedded game data resource '{resourceName}' was not found.");
    }
}
