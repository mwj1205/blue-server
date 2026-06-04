namespace blueServer.Game;

public static class SessionMonitor
{
    public static async Task StartAsync()
    {
        while (true)
        {
            foreach (var session in SessionManager.GetAll())
            {
                var diff = DateTime.UtcNow - session.LastReceiveTime;

                if (diff.TotalSeconds > 30)
                {
                    Console.WriteLine($"Timeout: {session.SessionId}");

                    session.Disconnect();
                }
            }

            await Task.Delay(10000);
        }
    }
}
