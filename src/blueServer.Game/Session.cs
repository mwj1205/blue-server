using System.Net.Sockets;
using System.Text;

namespace blueServer.Game;

public class Session
{
    private readonly TcpClient _client;

    public Session(TcpClient client)
    {
        _client = client;
    }

    public async Task StartAsync()
    {
        Console.WriteLine("Client Connected");

        var stream = _client.GetStream();

        var buffer = new byte[1024];

        while (true)
        {
            var length = await stream.ReadAsync(buffer);

            if (length == 0)
            {
                Console.WriteLine("Client Disconnected");
                break;
            }

            var message = Encoding.UTF8.GetString(
                buffer,
                0,
                length);

            Console.WriteLine($"Received: {message}");

            var response = Encoding.UTF8.GetBytes($"Echo: {message}");

            await stream.WriteAsync(response);
        }
    }
}