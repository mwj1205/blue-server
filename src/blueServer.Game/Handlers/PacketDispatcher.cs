using blueServer.Game.Packets;
using blueServer.Infrastructure.Observability;
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

        if (!CanDispatch(session, reader.Opcode))
        {
            _logger.LogWarning(
                LogEventIds.Game.UnauthenticatedPacketRejected,
                "Unauthenticated packet rejected. SessionId={SessionId}, PlayerId={PlayerId}, Opcode={Opcode}",
                session.SessionId,
                session.PlayerId,
                reader.Opcode);
            return;
        }

        _logger.LogDebug(
            LogEventIds.Game.PacketDispatchStarted,
            "Packet dispatch started. SessionId={SessionId}, PlayerId={PlayerId}, Opcode={Opcode}, PacketSize={PacketSize}",
            session.SessionId,
            session.PlayerId,
            reader.Opcode,
            reader.Size);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetKeyedService<IPacketHandler>(reader.Opcode);

        if (handler is not null)
        {
            await handler.HandleAsync(session, reader, cancellationToken);
            return;
        }

        _logger.LogWarning(
            LogEventIds.Game.UnhandledOpcodeReceived,
            "Unhandled opcode received. SessionId={SessionId}, PlayerId={PlayerId}, Opcode={Opcode}",
            session.SessionId,
            session.PlayerId,
            reader.Opcode);
    }

    private static bool CanDispatch(Session session, Opcode opcode)
    {
        return session.IsAuthenticated ||
            opcode is Opcode.Login or Opcode.Ping;
    }
}
