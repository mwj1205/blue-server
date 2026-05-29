using System.Security.Cryptography.X509Certificates;
using blueServer.Game.Packets;

namespace blueServer.Game.Handlers;

public static class PacketDispatcher
{
    private static readonly Dictionary<
        Opcode,
        IPacketHandler> _handlers = new();

    static PacketDispatcher()
    {
        Register(new LoginHandler());
        Register(new ChatHandler());
    }

    // 핸들러 등록
    private static void Register(IPacketHandler handler)
    {
        _handlers[handler.Opcode] = handler;
    }

    // Opcode 찾아서 Handler 실행
    public static async Task DispatchAsync(Session session, PacketReader reader)
    {
        if (_handlers.TryGetValue(reader.Opcode, out var handler))
        {
            await handler.HandleAsync(session, reader);
            return;
        }
        Console.WriteLine($"Unhandled Opcode: {reader.Opcode}");
    }
}
