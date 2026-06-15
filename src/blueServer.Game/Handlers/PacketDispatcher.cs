using blueServer.Game.Packets;
using Microsoft.Extensions.DependencyInjection;

namespace blueServer.Game.Handlers;

public sealed class PacketDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PacketDispatcher(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task DispatchAsync(Session session, PacketReader reader)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handlers = scope.ServiceProvider.GetRequiredService<IEnumerable<IPacketHandler>>();
        var handler = handlers.FirstOrDefault(candidate => candidate.Opcode == reader.Opcode);

        if (handler is not null)
        {
            await handler.HandleAsync(session, reader);
            return;
        }

        Console.WriteLine($"Unhandled Opcode: {reader.Opcode}");
    }
}
