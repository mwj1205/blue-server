using blueServer.Game.Packets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace blueServer.Game.Handlers;

public sealed class PacketDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PacketDispatcher> _logger;

    public PacketDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<PacketDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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

        _logger.LogWarning(
            "Unhandled opcode received. SessionId={SessionId}, PlayerId={PlayerId}, Opcode={Opcode}",
            session.SessionId,
            session.PlayerId,
            reader.Opcode);
    }
}
