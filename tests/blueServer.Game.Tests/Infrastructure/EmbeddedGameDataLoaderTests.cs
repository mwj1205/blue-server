using blueServer.Domain.Entities;
using blueServer.Domain.Items;
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
        Assert.Equal(4, gameData.ItemCatalog.Templates.Count);
        Assert.Equal(
            4,
            gameData.ItemCatalog.Templates
                .Select(item => item.Id)
                .Distinct()
                .Count());

        var material = Assert.Single(
            gameData.ItemCatalog.Templates,
            item => item.Id == 1001);

        Assert.Equal("growth_material_basic", material.Code);
        Assert.Equal(ItemType.Material, material.Type);
        Assert.Equal(ItemTemplate.InventoryMaxQuantity, material.MaxQuantity);
        Assert.Equal(
            "초급 성장 재료",
            gameData.DefaultLocalization.GetRequiredText(material.NameKey));
        Assert.Equal(
            "캐릭터 성장에 사용하는 기본 재료입니다.",
            gameData.DefaultLocalization.GetRequiredText(material.DescriptionKey));
    }
}
