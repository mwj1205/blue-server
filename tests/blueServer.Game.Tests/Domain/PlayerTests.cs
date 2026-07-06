using blueServer.Domain.Entities;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class PlayerTests
{
    [Fact]
    public void Create_SetsInitialCurrency()
    {
        var player = Player.Create("sensei");

        Assert.Equal("sensei", player.Nickname);
        Assert.Equal(Player.InitialGold, player.Gold);
        Assert.Equal(Player.InitialGem, player.Gem);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_Throws_WhenNicknameIsBlank(string nickname)
    {
        Assert.Throws<ArgumentException>(
            () => Player.Create(nickname));
    }

    [Fact]
    public void TrySpendGems_DecreasesGem_WhenEnoughGemExists()
    {
        var player = Player.Create("sensei");

        var result = player.TrySpendGems(100);

        Assert.True(result);
        Assert.Equal(Player.InitialGem - 100, player.Gem);
    }

    [Fact]
    public void TrySpendGems_KeepsGem_WhenGemIsNotEnough()
    {
        var player = Player.Create("sensei");

        var result = player.TrySpendGems(Player.InitialGem + 1);

        Assert.False(result);
        Assert.Equal(Player.InitialGem, player.Gem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TrySpendGems_Throws_WhenAmountIsNotPositive(int amount)
    {
        var player = Player.Create("sensei");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => player.TrySpendGems(amount));
    }
}
