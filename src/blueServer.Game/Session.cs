using System.Net.Sockets;
using System.Text;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;

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

            var packet = PacketReader.Read(buffer, length);

            PacketHandler.Handle(packet);
        }
    }
}
