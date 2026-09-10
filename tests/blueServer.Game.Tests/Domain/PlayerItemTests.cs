using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class PlayerItemTests
{
    [Fact]
    public void Create_SetsOwnerTemplateAndQuantity()
    {
        var template = CreateTemplate();

        var item = PlayerItem.Create(1, template, 20);

        Assert.Equal(1, item.PlayerId);
        Assert.Equal(template.Id, item.ItemTemplateId);
        Assert.Equal(20, item.Quantity);
        Assert.Same(template, item.ItemTemplate);
    }

    [Fact]
    public void IncreaseUpToLimit_IncreasesEntireQuantityWithinLimit()
    {
        var item = PlayerItem.Create(1, CreateTemplate(), 20);

        var result = item.IncreaseUpToLimit(30);

        Assert.Equal(30, result.RequestedQuantity);
        Assert.Equal(30, result.AppliedQuantity);
        Assert.Equal(0, result.OverflowQuantity);
        Assert.Equal(50, result.QuantityAfter);
        Assert.False(result.HasOverflow);
        Assert.Equal(50, item.Quantity);
    }

    [Fact]
    public void IncreaseUpToLimit_IncreasesToLimitAndReturnsOverflow()
    {
        var item = PlayerItem.Create(
            1,
            CreateTemplate(),
            999_000);

        var result = item.IncreaseUpToLimit(1_200);

        Assert.Equal(1_200, result.RequestedQuantity);
        Assert.Equal(999, result.AppliedQuantity);
        Assert.Equal(201, result.OverflowQuantity);
        Assert.Equal(999_999, result.QuantityAfter);
        Assert.True(result.HasOverflow);
        Assert.Equal(999_999, item.Quantity);
    }

    [Fact]
    public void TryDecrease_DecreasesQuantityWhenSufficient()
    {
        var item = PlayerItem.Create(1, CreateTemplate(), 20);

        var changed = item.TryDecrease(15);

        Assert.True(changed);
        Assert.Equal(5, item.Quantity);
    }

    [Fact]
    public void TryDecrease_DoesNotChangeQuantityWhenInsufficient()
    {
        var item = PlayerItem.Create(1, CreateTemplate(), 20);

        var changed = item.TryDecrease(21);

        Assert.False(changed);
        Assert.Equal(20, item.Quantity);
    }

    [Fact]
    public void Create_RejectsQuantityAboveStackLimit()
    {
        var template = CreateTemplate();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerItem.Create(
                1,
                template,
                ItemTemplate.InventoryMaxQuantity + 1));
    }

    private static ItemTemplate CreateTemplate()
    {
        return ItemTemplate.Create(
            1001,
            "Beginner Activity Report",
            ItemType.Material);
    }
}
