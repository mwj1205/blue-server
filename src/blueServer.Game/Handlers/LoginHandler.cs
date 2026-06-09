using blueServer.Domain.Entities;
using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public class LoginHandler : IPacketHandler
{
    public Opcode Opcode => Opcode.Login;

    public async Task HandleAsync(Session session, PacketReader reader)
    {
        var nickname = reader.ReadString();
        var password = reader.ReadString();
        Console.WriteLine($"[Login] {nickname}");
        Console.WriteLine($"[Login] {password}");

        var player = new Player
        {
            Id = 1,
            Nickname = nickname
        };

        session.Login(player);

        var result = new LoginResultPacket
        {
            Success = true,
            Message = "Login Success"
        };

        await session.SendAsync(result.Serialize());
    }
}
