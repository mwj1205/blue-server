using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public sealed class ChatHandler : IPacketHandler
{
    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!session.IsAuthenticated)
        {
            Console.WriteLine("Unauthorized Chat");
            return;
        }

        var message = reader.ReadString();
        Console.WriteLine($"[{session.PlayerNickname}] {message}");

        var packet = new ChatMessagePacket { Message = $"[{session.PlayerNickname}]: {message}" };
        await SessionManager.BroadcastAsync(packet.Serialize(), cancellationToken);
    }
}
