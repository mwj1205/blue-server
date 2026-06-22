using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;

namespace blueServer.Game;

public class TcpListenerService : BackgroundService
{
    private readonly SessionFactory _factory;
    private readonly TcpListener _listener;

    public TcpListenerService(SessionFactory factory)
    {
        _factory = factory;
        _listener = new TcpListener(IPAddress.Any, 7777);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener.Start();
        Console.WriteLine("TCP Server Listener Started on port 7777...");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);

                // 생성을 위임받은 팩토리를 통해 세션 객체 획득
                var session = _factory.Create(client);
                SessionManager.Add(session);
                _ = session.StartAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("TCP Listener stopped.");
        }
    }
}
