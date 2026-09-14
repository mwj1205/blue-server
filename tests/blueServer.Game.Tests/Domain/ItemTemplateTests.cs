using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class ItemTemplateTests
{
    [Fact]
    public void Create_NormalizesCatalogFieldsAndSetsInventoryRules()
    {
        var template = ItemTemplate.Create(
            1001,
            "  growth_material_basic  ",
            "  item.growth_material.basic.name  ",
            "  item.growth_material.basic.description  ",
            ItemType.Material);

        Assert.Equal(1001, template.Id);
        Assert.Equal("growth_material_basic", template.Code);
        Assert.Equal(
            "item.growth_material.basic.name",
            template.NameKey);
        Assert.Equal(
            "item.growth_material.basic.description",
            template.DescriptionKey);
        Assert.Equal(ItemType.Material, template.Type);
        Assert.Equal(
            ItemTemplate.InventoryMaxQuantity,
            template.MaxQuantity);
        Assert.True(template.IsActive);
    }

    [Theory]
    [InlineData(null, "item.material.name", "item.material.description", "code")]
    [InlineData("material", null, "item.material.description", "nameKey")]
    [InlineData("material", "item.material.name", null, "descriptionKey")]
    public void Create_RejectsMissingCatalogField(
        string? code,
        string? nameKey,
        string? descriptionKey,
        string expectedParameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ItemTemplate.Create(
                1001,
                code!,
                nameKey!,
                descriptionKey!,
                ItemType.Material));

        Assert.Equal(expectedParameterName, exception.ParamName);
    }

    [Fact]
    public void Create_RejectsUnsupportedItemType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ItemTemplate.Create(
                1001,
                "growth_material_basic",
                "item.growth_material.basic.name",
                "item.growth_material.basic.description",
                (ItemType)999));
    }

    [Fact]
    public void UpdateDefinition_UpdatesMutableFieldsAndReactivatesTemplate()
    {
        var template = CreateTemplate();
        template.Deactivate();

        var changed = template.UpdateDefinition(
            "item.material.updated.name",
            "item.material.updated.description",
            ItemType.Consumable);

        Assert.True(changed);
        Assert.Equal("growth_material_basic", template.Code);
        Assert.Equal("item.material.updated.name", template.NameKey);
        Assert.Equal(
            "item.material.updated.description",
            template.DescriptionKey);
        Assert.Equal(ItemType.Consumable, template.Type);
        Assert.True(template.IsActive);
    }

    [Fact]
    public void UpdateDefinition_ReturnsFalseWhenDefinitionIsUnchanged()
    {
        var template = CreateTemplate();

        var changed = template.UpdateDefinition(
            template.NameKey,
            template.DescriptionKey,
            template.Type);

        Assert.False(changed);
    }

    [Fact]
    public void Deactivate_IsIdempotent()
    {
        var template = CreateTemplate();

        Assert.True(template.Deactivate());
        Assert.False(template.Deactivate());
        Assert.False(template.IsActive);
    }

    private static ItemTemplate CreateTemplate()
    {
        return ItemTemplate.Create(
            1001,
            "growth_material_basic",
            "item.growth_material.basic.name",
            "item.growth_material.basic.description",
            ItemType.Material);
    }
}
