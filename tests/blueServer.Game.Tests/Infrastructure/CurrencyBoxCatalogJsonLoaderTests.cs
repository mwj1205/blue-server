using System.Text;
using System.Text.Json;
using blueServer.Domain.Currencies;
using blueServer.Infrastructure.GameData;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class CurrencyBoxCatalogJsonLoaderTests
{
    [Theory]
    [InlineData("Gold", "Fixed", 100, 100)]
    [InlineData("Gem", "UniformRandom", 20, 200)]
    public async Task LoadAsync_PreservesRewardDefinition(string currency, string mode, int min, int max)
    {
        var catalog = await LoadAsync(Document(Box(3101, currency, mode, min, max)));

        Assert.Equal(new CurrencyBoxDefinition(
            3101, Enum.Parse<CurrencyType>(currency), Enum.Parse<CurrencyBoxAmountMode>(mode), min, max),
            Assert.Single(catalog.Boxes));
    }

    [Theory]
    [InlineData("Gold", "Fixed", 0, 0)]
    [InlineData("Gold", "Fixed", 100, 200)]
    [InlineData("Gem", "UniformRandom", 200, 20)]
    [InlineData("Gem", "UniformRandom", 100, 100)]
    [InlineData("Energy", "Fixed", 100, 100)]
    [InlineData("Gold", "WeightedRandom", 20, 200)]
    public async Task LoadAsync_RejectsInvalidReward(string currency, string mode, int min, int max)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LoadAsync(Document(Box(3101, currency, mode, min, max))));
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateAndInvalidIds()
    {
        var box = Box(3101, "Gold", "Fixed", 100, 100);
        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(Document(box, box)));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LoadAsync(Document(Box(0, "Gold", "Fixed", 100, 100))));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":2,\"boxes\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"boxes\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"boxes\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"boxes\":[{\"itemTemplateId\":3101}]}")]
    [InlineData("{\"schemaVersion\":1,\"boxes\":[],\"unknown\":true}")]
    public async Task LoadAsync_RejectsInvalidDocument(string json)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => LoadAsync(json));
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(999999)]
    public async Task GameDataCatalog_RejectsMaterialOrMissingBoxReference(int itemId)
    {
        var catalog = await new EmbeddedGameDataLoader().LoadAsync();
        var boxes = new CurrencyBoxCatalog(1,
            [new CurrencyBoxDefinition(itemId, CurrencyType.Gold, CurrencyBoxAmountMode.Fixed, 100, 100)]);

        Assert.Throws<InvalidDataException>(() => GameDataCatalog.Create(
            catalog.ItemCatalog, catalog.DefaultLocalization, boxes));
    }

    private static object Box(int id, string currency, string mode, int min, int max) => new
    {
        itemTemplateId = id,
        currencyType = currency,
        amountMode = mode,
        minAmount = min,
        maxAmount = max
    };

    private static string Document(params object[] boxes) =>
        JsonSerializer.Serialize(new { schemaVersion = 1, boxes });

    private static async Task<CurrencyBoxCatalog> LoadAsync(string json)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await CurrencyBoxCatalogJsonLoader.LoadAsync(stream);
    }
}
