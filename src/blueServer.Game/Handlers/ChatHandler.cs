using blueServer.Game.Packets;
using Microsoft.Extensions.Logging;

namespace blueServer.Game.Handlers;

public sealed class ChatHandler : IPacketHandler
{
    private readonly SessionManager _sessionManager;
    private readonly ILogger<ChatHandler> _logger;

    public ChatHandler(
        SessionManager sessionManager,
        ILogger<ChatHandler> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!session.IsAuthenticated)
        {
            _logger.LogWarning(
                "Unauthorized chat packet rejected. SessionId={SessionId}",
                session.SessionId);
            return;
        }

        var message = reader.ReadString();

        _logger.LogInformation(
            "Chat packet received. SessionId={SessionId}, PlayerId={PlayerId}, MessageLength={MessageLength}",
            session.SessionId,
            session.PlayerId,
            message.Length);

        var packet = new ChatMessagePacket { Message = $"[{session.PlayerNickname}]: {message}" };
        await _sessionManager.BroadcastAsync(packet.Serialize(), cancellationToken);
    }
}
