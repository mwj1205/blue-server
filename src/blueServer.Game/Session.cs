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

        try
        {
            while (true)
            {
                // 클라이언트로부터 데이터 수신 대기
                var length = await stream.ReadAsync(buffer);

                if (length == 0)
                {
                    Console.WriteLine("Client Disconnected");
                    break;
                }

                var packet = PacketReader.Read(buffer, length);

                // 패킷을 알맞은 비즈니스 핸들러로 라우팅
                PacketHandler.Handle(packet);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Session Error: {ex.Message}");
        }
    }
}
