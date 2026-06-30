using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public class TcpListenerService : BackgroundService
{
    private readonly SessionFactory _factory;
    private readonly SessionTaskTracker _sessionTaskTracker;
    private readonly ILogger<TcpListenerService> _logger;
    private readonly TcpListener _listener;

    public TcpListenerService(
        SessionFactory factory,
        SessionTaskTracker sessionTaskTracker,
        ILogger<TcpListenerService> logger)
    {
        _factory = factory;
        _sessionTaskTracker = sessionTaskTracker;
        _logger = logger;
        _listener = new TcpListener(IPAddress.Any, 7777);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _logger.LogInformation("TCP server listener started on port {Port}.", 7777);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);

                // 생성을 위임받은 팩토리를 통해 세션 객체 획득
                var session = _factory.Create(client);
                SessionManager.Add(session);
                var sessionTask = session.StartAsync(stoppingToken);
                _sessionTaskTracker.Track(session, sessionTask);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TCP listener stopped.");
        }
    }
}
