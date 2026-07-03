using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public class PingHandler : IPacketHandler
{
    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pong = new PongPacket();

        await session.SendAsync(pong.Serialize(), cancellationToken);
    }
}
