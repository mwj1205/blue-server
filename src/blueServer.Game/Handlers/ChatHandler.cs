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

        var message = reader.ReadString();
        var playerNickname = session.PlayerNickname ??
            throw new InvalidOperationException("Chat handler requires authenticated session.");

        _logger.LogInformation(
            "Chat packet received. SessionId={SessionId}, PlayerId={PlayerId}, MessageLength={MessageLength}",
            session.SessionId,
            session.PlayerId,
            message.Length);

        var packet = new ChatMessagePacket { Message = $"[{playerNickname}]: {message}" };
        await _sessionManager.BroadcastAsync(packet.Serialize(), cancellationToken);
    }
}
