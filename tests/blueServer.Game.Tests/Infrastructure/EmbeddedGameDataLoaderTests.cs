using blueServer.Infrastructure.GameData;
using blueServer.Domain.Currencies;
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
        Assert.Equal(new[]
        {
            new CurrencyBoxDefinition(3101, CurrencyType.Gold, CurrencyBoxAmountMode.Fixed, 100, 100),
            new CurrencyBoxDefinition(3102, CurrencyType.Gem, CurrencyBoxAmountMode.Fixed, 100, 100),
            new CurrencyBoxDefinition(3103, CurrencyType.Gold, CurrencyBoxAmountMode.UniformRandom, 20, 200),
            new CurrencyBoxDefinition(3104, CurrencyType.Gem, CurrencyBoxAmountMode.UniformRandom, 20, 200)
        }, gameData.CurrencyBoxCatalog.Boxes);
    }
}
