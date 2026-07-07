using blueServer.Game.Packets;
using Xunit;

namespace blueServer.Game.Tests.Packets;

public sealed class LoginRequestPacketTests
{
    [Fact]
    public void Read_ReturnsAccessToken_WhenPacketContainsToken()
    {
        const string accessToken = "access-token";
        var packet = new LoginRequestPacket
        {
            AccessToken = accessToken
        }.Serialize();

        var reader = new PacketReader(packet);
        var request = LoginRequestPacket.Read(reader);

        Assert.Equal(Opcode.Login, reader.Opcode);
        Assert.Equal(accessToken, request.AccessToken);
    }
}
