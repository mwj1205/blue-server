using blueServer.Domain.Entities;
using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public class LoginHandler : IPacketHandler
{
    public Opcode Opcode => Opcode.Login;

    public Task HandleAsync(Session session, PacketReader reader)
    {
        var nickname = reader.ReadString();
        Console.WriteLine($"[Login] {nickname}");

        var player = new Player
        {
            Id = 1,
            Nickname = nickname
        };

        session.Login(player);

        return Task.CompletedTask;
    }
}
