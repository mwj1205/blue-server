using System.Text;
using blueServer.Infrastructure.GameData;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class ItemCatalogJsonLoaderTests
{
    [Fact]
    public async Task LoadAsync_NormalizesCodesAndLocalizationKeys()
    {
        await using var stream = CreateStream(
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "id": 1001,
                  "code": "  growth_material_basic  ",
                  "nameKey": "  item.growth_material.basic.name  ",
                  "descriptionKey": "  item.growth_material.basic.description  ",
                  "type": "Material"
                }
              ]
            }
            """);

        var catalog = await ItemCatalogJsonLoader.LoadAsync(stream);
        var item = Assert.Single(catalog.Templates);

        Assert.Equal("growth_material_basic", item.Code);
        Assert.Equal("item.growth_material.basic.name", item.NameKey);
        Assert.Equal(
            "item.growth_material.basic.description",
            item.DescriptionKey);
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateIds()
    {
        await using var stream = CreateStream(
            CreateItemJson(
                """{ "id": 1001, "code": "material_a", "nameKey": "a.name", "descriptionKey": "a.description", "type": "Material" }""",
                """{ "id": 1001, "code": "material_b", "nameKey": "b.name", "descriptionKey": "b.description", "type": "Material" }"""));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => ItemCatalogJsonLoader.LoadAsync(stream));

        Assert.Contains("duplicate id 1001", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateCodes()
    {
        await using var stream = CreateStream(
            CreateItemJson(
                """{ "id": 1001, "code": "material", "nameKey": "a.name", "descriptionKey": "a.description", "type": "Material" }""",
                """{ "id": 1002, "code": "material", "nameKey": "b.name", "descriptionKey": "b.description", "type": "Material" }"""));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => ItemCatalogJsonLoader.LoadAsync(stream));

        Assert.Contains("duplicate code 'material'", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnknownItemType()
    {
        await using var stream = CreateStream(
            CreateItemJson(
                """{ "id": 1001, "code": "invalid", "nameKey": "invalid.name", "descriptionKey": "invalid.description", "type": "Unknown" }"""));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => ItemCatalogJsonLoader.LoadAsync(stream));
    }

    private static string CreateItemJson(params string[] items)
    {
        return $$"""
            {
              "schemaVersion": 1,
              "items": [
                {{string.Join(",", items)}}
              ]
            }
            """;
    }

    private static MemoryStream CreateStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }
}
