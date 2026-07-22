using System.Net;
using System.Net.Sockets;
using blueServer.Game.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace blueServer.Game;

public class TcpListenerService : BackgroundService
{
    private static readonly TimeSpan SessionShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly SessionFactory _factory;
    private readonly SessionManager _sessionManager;
    private readonly SessionTaskTracker _sessionTaskTracker;
    private readonly ILogger<TcpListenerService> _logger;
    private readonly TcpListener _listener;
    private readonly int _port;

    public TcpListenerService(
        SessionFactory factory,
        SessionManager sessionManager,
        SessionTaskTracker sessionTaskTracker,
        IOptions<GameServerOptions> options,
        ILogger<TcpListenerService> logger)
    {
        _factory = factory;
        _sessionManager = sessionManager;
        _sessionTaskTracker = sessionTaskTracker;
        _logger = logger;
        _port = options.Value.Port;
        _listener = new TcpListener(IPAddress.Any, _port);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        _logger.LogInformation("TCP server listener started on port {Port}.", _port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);

                // 생성을 위임받은 팩토리를 통해 세션 객체 획득
                var session = _factory.Create(client);
                _sessionManager.Add(session);
                var sessionTask = RunSessionAsync(session, stoppingToken);
                _sessionTaskTracker.Track(session, sessionTask);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("TCP listener stopped.");
        }
        finally
        {
            _listener.Stop();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "TCP server shutdown requested. ActiveSessionCount={ActiveSessionCount}",
            _sessionTaskTracker.ActiveSessionCount);

        await base.StopAsync(cancellationToken);

        var sessions = _sessionManager.GetAll();

        foreach (var session in sessions)
        {
            session.Disconnect();
        }

        try
        {
            var allSessionsStopped = await _sessionTaskTracker.WaitForAllAsync(
                SessionShutdownTimeout,
                cancellationToken);

            if (allSessionsStopped)
            {
                _logger.LogInformation("All TCP sessions stopped.");
                return;
            }

            _logger.LogWarning(
                "Timed out while waiting for TCP sessions to stop. ActiveSessionCount={ActiveSessionCount}, TimeoutSeconds={TimeoutSeconds}",
                _sessionTaskTracker.ActiveSessionCount,
                SessionShutdownTimeout.TotalSeconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "TCP server shutdown was canceled before all sessions stopped. ActiveSessionCount={ActiveSessionCount}",
                _sessionTaskTracker.ActiveSessionCount);
        }
    }

    private async Task RunSessionAsync(
        Session session,
        CancellationToken cancellationToken)
    {
        try
        {
            await session.StartAsync(cancellationToken);
        }
        finally
        {
            // 세션을 등록한 쪽에서 제거까지 책임져야 Session이 SessionManager를 알지 않아도 된다.
            _sessionManager.Remove(session);
            session.Dispose();
        }
    }
}
