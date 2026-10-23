using blueServer.Infrastructure.GameData;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class EmbeddedGameDataLoaderTests
{
    [Fact]
    public async Task LoadAsync_LoadsItemsAndDefaultLocalization()
    {
        var loader = new EmbeddedGameDataLoader();

        var gameData = await loader.LoadAsync();

        Assert.Equal(
            ItemCatalogJsonLoader.SupportedSchemaVersion,
            gameData.ItemCatalog.SchemaVersion);
        Assert.Equal("ko-KR", gameData.DefaultLocalization.Locale);
        Assert.NotEmpty(gameData.ItemCatalog.Templates);
        Assert.All(gameData.ItemCatalog.Templates, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(
                gameData.DefaultLocalization.GetRequiredText(item.NameKey)));
            Assert.False(string.IsNullOrWhiteSpace(
                gameData.DefaultLocalization.GetRequiredText(item.DescriptionKey)));
        });
    }
}
