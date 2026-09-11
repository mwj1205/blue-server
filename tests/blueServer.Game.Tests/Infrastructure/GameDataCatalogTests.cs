using System.Collections.ObjectModel;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure.GameData;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class GameDataCatalogTests
{
    [Fact]
    public void Create_RejectsMissingLocalizationKey()
    {
        var itemCatalog = new ItemCatalog(
            1,
            [
                new ItemTemplateDefinition(
                    1001,
                    "material",
                    "item.material.name",
                    "item.material.description",
                    ItemType.Material,
                    ItemTemplate.InventoryMaxQuantity)
            ]);
        var localization = new LocalizationCatalog(
            1,
            "ko-KR",
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>
                {
                    ["item.material.name"] = "성장 재료"
                }));

        var exception = Assert.Throws<InvalidDataException>(
            () => GameDataCatalog.Create(itemCatalog, localization));

        Assert.Contains("item.material.description", exception.Message);
    }
}
