using System.Net.Sockets;
using System.Collections.Concurrent;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using blueServer.Domain.Entities;

namespace blueServer.Game;

public class Session
{
    private readonly TcpClient _client;

    private readonly ReceiveBuffer _receiveBuffer = new(4096);

    // 전송 대기중인 패킷 저장할 큐
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private bool _sending;

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
    public Task SendAsync(byte[] data)
    {
        // 바로 전송이 아니라 데이터를 전송 큐에 삽입
        _sendQueue.Enqueue(data);

        if (_sending)
        {
            return Task.CompletedTask;
        }

        _ = Task.Run(SendLoopAsync);

        return Task.CompletedTask;
    }

    private async Task SendLoopAsync()
    {
        _sending = true;

        try
        {
            var stream = _client.GetStream();

            while (_sendQueue.TryDequeue(out var packet))
            {
                await stream.WriteAsync(packet);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Send Error: {ex}");
        }
        finally
        {
            _sending = false;

            // 송신 종료 직전에 새 패킷이 들어온 경우
            if (!_sendQueue.IsEmpty)
            {
                _ = Task.Run(SendLoopAsync);
            }
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

                _receiveBuffer.Write(buffer, length);

                while (true)
                {
                    // 패킷 크기 읽으려면 최소 2byte 필요
                    if (_receiveBuffer.Length < 2) break;

                    var packetSize = BitConverter.ToUInt16(_receiveBuffer.Buffer, 0);

                    // 아직 패킷이 덜 도착함
                    if (_receiveBuffer.Length < packetSize) break;

                    var packetData = new byte[packetSize];

                    Array.Copy(
                        _receiveBuffer.Buffer,
                        0,
                        packetData,
                        0,
                        packetSize
                    );

                    var reader = new PacketReader(packetData);
                    await PacketDispatcher.DispatchAsync(this, reader);

                    _receiveBuffer.Remove(packetSize);
                }

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
