using blueServer.Domain.Rewards;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class CurrencyRewardTests
{
    [Theory]
    [InlineData(RewardType.Gold, 100)]
    [InlineData(RewardType.Gem, 10)]
    public void Create_SetsValidatedValues(
        RewardType type,
        int amount)
    {
        var reward = CurrencyReward.Create(type, amount);

        Assert.Equal(type, reward.Type);
        Assert.Equal(amount, reward.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Throws_WhenAmountIsNotPositive(int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurrencyReward.Create(RewardType.Gold, amount));
    }

    [Fact]
    public void Create_Throws_WhenTypeIsUnknown()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CurrencyReward.Create((RewardType)999, 1));
    }

    [Fact]
    public void BundleCreate_RequiresAtLeastOneItem()
    {
        Assert.Throws<ArgumentException>(
            () => RewardBundle.Create([]));
    }
}
