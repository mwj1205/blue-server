using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public class ChatHandler : IPacketHandler
{
    public Opcode Opcode => Opcode.Chat;

    public async Task HandleAsync(Session session, PacketReader reader)
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
    }
}
