using System.Net.Sockets;
using blueServer.Game.Handlers;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public class SessionFactory
{
    private readonly PacketDispatcher _dispatcher;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<Session> _logger;

    public SessionFactory(
        PacketDispatcher dispatcher,
        SessionManager sessionManager,
        ILogger<Session> logger)
    {
        _dispatcher = dispatcher;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public Session Create(TcpClient client)
    {
        return new Session(
            client,
            _dispatcher,
            _sessionManager,
            _logger);
    }
}
