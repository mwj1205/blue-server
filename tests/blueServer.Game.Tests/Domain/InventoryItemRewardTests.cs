using blueServer.Domain.Rewards;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class InventoryItemRewardTests
{
    [Fact]
    public void Create_SetsTemplateAndQuantity()
    {
        var reward = InventoryItemReward.Create(1001, 25);

        Assert.Equal(1001, reward.ItemTemplateId);
        Assert.Equal(25, reward.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsInvalidTemplateId(int itemTemplateId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryItemReward.Create(itemTemplateId, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsInvalidQuantity(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryItemReward.Create(1001, quantity));
    }
}
