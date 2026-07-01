namespace blueServer.Game;

public sealed class SessionMonitor
{
    private readonly SessionManager _sessionManager;

    public SessionMonitor(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
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
                    Console.WriteLine($"Timeout: {session.SessionId}");
                    session.Disconnect();
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
