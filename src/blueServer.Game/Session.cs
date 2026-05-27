using System.Net.Sockets;
using System.Text;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.VisualBasic;

namespace blueServer.Game;

public class Session
{
    private readonly TcpClient _client;
    public Guid SessionId { get; }
    public Session(TcpClient client)
    {
        _client = client;

        SessionId = Guid.NewGuid();
    }

    public async Task StartAsync()
    {
        SessionManager.Add(this);

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

                    SessionManager.Remove(this);
                    break;
                }

                var data = buffer[..length];

                var reader = new PacketReader(data);

                // 패킷을 알맞은 비즈니스 핸들러로 라우팅
                await PacketHandler.Handle(reader);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Session Error: {ex.Message}");
        }
    }

    public async Task SendAsync(byte[] data)
    {
        var stream = _client.GetStream();

        await stream.WriteAsync(data);
    }
}
