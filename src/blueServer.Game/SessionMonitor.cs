using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public sealed class SessionMonitor
{
    private readonly SessionManager _sessionManager;
    private readonly ILogger<SessionMonitor> _logger;

    public SessionMonitor(
        SessionManager sessionManager,
        ILogger<SessionMonitor> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var session in _sessionManager.GetAll())
            {
                var diff = DateTime.UtcNow - session.LastReceiveTime;

                if (diff.TotalSeconds > 30)
                {
                    _logger.LogWarning(
                        "Session timed out. SessionId={SessionId}, PlayerId={PlayerId}, IdleSeconds={IdleSeconds}",
                        session.SessionId,
                        session.PlayerId,
                        diff.TotalSeconds);
                    session.Disconnect();
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
