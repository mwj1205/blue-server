using blueServer.Game.Packets;
using blueServer.Infrastructure.Observability;
using Elastic.Apm;
using Elastic.Apm.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace blueServer.Game.Handlers;

public sealed class PacketDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PacketDispatcher> _logger;
    private readonly IApmAgent? _apmAgent;

    public PacketDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<PacketDispatcher> logger,
        IApmAgent? apmAgent = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _apmAgent = apmAgent;
    }

    public async Task DispatchAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transaction = _apmAgent?.Tracer.StartTransaction(
            $"TCP {reader.Opcode}",
            "tcp");

        if (transaction is not null)
        {
            transaction.SetLabel(
                "session_id",
                session.SessionId.ToString("N"));
            transaction.SetLabel("opcode", (int)reader.Opcode);
            transaction.SetLabel("packet_size", reader.Size);

            if (session.PlayerId is long playerId)
            {
                transaction.SetLabel("player_id", playerId);
            }
        }

        try
        {
            await DispatchCoreAsync(
                session,
                reader,
                transaction,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (transaction is not null)
            {
                transaction.Result = "canceled";
                transaction.Outcome = Outcome.Unknown;
            }

            throw;
        }
        catch (Exception ex)
        {
            if (transaction is not null)
            {
                transaction.CaptureException(ex);
                transaction.Result = "failed";
                transaction.Outcome = Outcome.Failure;
            }

            throw;
        }
        finally
        {
            transaction?.End();
        }
    }

    private async Task DispatchCoreAsync(
        Session session,
        PacketReader reader,
        ITransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (!CanDispatch(session, reader.Opcode))
        {
            _logger.LogWarning(
                LogEventIds.Game.UnauthenticatedPacketRejected,
                "Unauthenticated packet rejected. SessionId={SessionId}, PlayerId={PlayerId}, Opcode={Opcode}",
                session.SessionId,
                session.PlayerId,
                reader.Opcode);

            if (transaction is not null)
            {
                transaction.Result = "rejected";
                transaction.Outcome = Outcome.Failure;
            }

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

            if (transaction is not null)
            {
                transaction.Result = "handled";
                transaction.Outcome = Outcome.Success;
            }

            return;
        }

        _logger.LogWarning(
            LogEventIds.Game.UnhandledOpcodeReceived,
            "Unhandled opcode received. SessionId={SessionId}, PlayerId={PlayerId}, Opcode={Opcode}",
            session.SessionId,
            session.PlayerId,
            reader.Opcode);

        if (transaction is not null)
        {
            transaction.Result = "unhandled";
            transaction.Outcome = Outcome.Failure;
        }
    }

    private static bool CanDispatch(Session session, Opcode opcode)
    {
        return session.IsAuthenticated ||
            opcode is Opcode.Login or Opcode.Ping;
    }
}
