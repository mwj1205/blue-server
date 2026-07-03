using System.Net.Sockets;
using blueServer.Game.Handlers;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public class SessionFactory
{
    private readonly PacketDispatcher _dispatcher;
    private readonly ILogger<Session> _logger;

    public SessionFactory(
        PacketDispatcher dispatcher,
        ILogger<Session> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public Session Create(TcpClient client)
    {
        return new Session(
            client,
            _dispatcher,
            _logger);
    }
}
