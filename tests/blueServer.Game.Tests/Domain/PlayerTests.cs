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
}
