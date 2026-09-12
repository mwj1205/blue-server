using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class ItemTemplateTests
{
    [Fact]
    public void Create_NormalizesNameAndSetsInventoryRules()
    {
        var template = ItemTemplate.Create(
            1001,
            "  Beginner Activity Report  ",
            ItemType.Material);

        Assert.Equal(1001, template.Id);
        Assert.Equal("Beginner Activity Report", template.Name);
        Assert.Equal(ItemType.Material, template.Type);
        Assert.Equal(
            ItemTemplate.InventoryMaxQuantity,
            template.MaxQuantity);
    }

    [Fact]
    public void Create_RejectsUnsupportedItemType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ItemTemplate.Create(
                1001,
                "Beginner Activity Report",
                (ItemType)999));
    }
}
