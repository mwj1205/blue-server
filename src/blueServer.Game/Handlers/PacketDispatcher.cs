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

    public async Task DispatchAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetKeyedService<IPacketHandler>(reader.Opcode);

        if (handler is not null)
        {
            await handler.HandleAsync(session, reader, cancellationToken);
            return;
        }

        Console.WriteLine($"Unhandled Opcode: {reader.Opcode}");
    }
}
