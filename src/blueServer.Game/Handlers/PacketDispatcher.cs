using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public class PacketDispatcher
{
    private readonly Dictionary<Opcode, IPacketHandler> _handlers = new();

    public PacketDispatcher(
        LoginHandler loginHandler,
        ChatHandler chatHandler,
        PingHandler pingHandler)
    {
        Register(loginHandler);
        Register(chatHandler);
        Register(pingHandler);
    }

    private void Register(IPacketHandler handler)
    {
        _handlers[handler.Opcode] = handler;
    }

    public async Task DispatchAsync(Session session, PacketReader reader)
    {
        if (_handlers.TryGetValue(reader.Opcode, out var handler))
        {
            await handler.HandleAsync(session, reader);
            return;
        }

        Console.WriteLine($"Unhandled Opcode: {reader.Opcode}");
    }
}
