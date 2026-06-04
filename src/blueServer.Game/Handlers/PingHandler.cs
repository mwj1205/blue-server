using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public class PingHandler : IPacketHandler
{
    public Opcode Opcode => Opcode.Ping;

    public async Task HandleAsync(Session session, PacketReader reader)
    {
        var pong = new PongPacket();

        await session.SendAsync(pong.Serialize());
    }
}
