using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public static class PacketHandler
{
    public static async Task HandleAsync(
        Session session,
        PacketReader reader
        )
    {
        switch (reader.Opcode)
        {
            case Opcode.Login:
                {
                    var nickname = reader.ReadString();
                    Console.WriteLine($"[Login] {nickname}");

                    var player = new blueServer.Domain.Entities.Player
                    {
                        Id = 1,
                        Nickname = nickname
                    };

                    session.Login(player);
                    break;
                }

            case Opcode.Chat:
                {
                    if (session.Player is null)
                    {
                        Console.WriteLine("Unauthorized Chat");
                        return;
                    }

                    var message = reader.ReadString();
                    Console.WriteLine($"[{session.Player.Nickname}] {message}");

                    var packet = new ChatMessagePacket { Message = $"[{session.Player.Nickname}]: {message}" };
                    await SessionManager.BroadcastAsync(packet.Serialize());
                    break;
                }

            default:
                {
                    Console.WriteLine($"Unknown Opcode: {reader.Opcode}");
                    break;
                }
        }
    }
}
