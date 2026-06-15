using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public interface IPacketHandler
{
    Task HandleAsync(Session session, PacketReader reader);
}
