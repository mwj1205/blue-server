using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public interface IPacketHandler
{
    Opcode Opcode { get; }

    Task HandleAsync(Session session, PacketReader reader);
}