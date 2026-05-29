using System.Net.Sockets;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using blueServer.Domain.Entities;

namespace blueServer.Game;

public class Session
{
    private readonly TcpClient _client;

    // 세션을 구별할 고유 ID
    public Guid SessionId { get; }

    public Player? Player { get; private set; }

    public Session(TcpClient client)
    {
        _client = client;
        SessionId = Guid.NewGuid();  // 세션이 생성될 때 고유 ID 할당
    }

    public void Login(Player player)
    {
        Player = player;

        Console.WriteLine($"Player Login: {player.Nickname}");
    }

    // 클라이언트로 바이너리 데이터를 전송하는 메서드
    public async Task SendAsync(byte[] data)
    {
        if (!_client.Connected) return;

        try
        {
            var stream = _client.GetStream();
            await stream.WriteAsync(data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Session] 전송 에러: {ex.Message}");
        }
    }

    // 세션 통신 루프 시작점
    public async Task StartAsync()
    {
        // 접속 성공 시 세션 매니저에 등록
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

                if (length == 0) break; // 연결 끊으면 루프 탈출

                // 받은 데이터 크기만큼 바이트 배열을 잘라서 패킷파서에 전달
                var data = buffer[..length];
                var reader = new PacketReader(data);

                // 패킷을 알맞은 비즈니스 핸들러로 라우팅
                await PacketHandler.HandleAsync(this, reader);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Session Error: {ex.Message}");
        }
        finally
        {
            // 연결이 종료되었을 때 매니저에서 제거하고 소켓 폐쇄
            SessionManager.Remove(this);
            Console.WriteLine($"Client Disconnected: {SessionId}");
            _client.Close();
        }
    }
}
