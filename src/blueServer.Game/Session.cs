using System.Buffers.Binary;
using System.Net.Sockets;
using System.Collections.Concurrent;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public class Session : IDisposable
{
    private const int MaxPacketSize = 4096;
    private const int ReadBufferSize = 1024;
    private const int ReceiveBufferCapacity = MaxPacketSize + ReadBufferSize;

    private readonly TcpClient _client;
    private readonly PacketDispatcher _dispatcher;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<Session> _logger;
    private readonly ReceiveBuffer _receiveBuffer = new(ReceiveBufferCapacity);
    private readonly CancellationTokenSource _disconnectCts = new();

    // 전송 대기중인 패킷 저장할 큐
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private int _sendLoopRunning;
    private int _disconnected;
    private int _disposed;

    // 세션을 구별할 고유 ID
    public Guid SessionId { get; }
    public long? PlayerId { get; private set; }
    public string? PlayerNickname { get; private set; }
    public bool IsAuthenticated => PlayerId.HasValue;
    public DateTime LastReceiveTime { get; private set; } = DateTime.UtcNow;

    public Session(
        TcpClient client,
        PacketDispatcher dispatcher,
        SessionManager sessionManager,
        ILogger<Session> logger)
    {
        _client = client;
        _dispatcher = dispatcher;
        _sessionManager = sessionManager;
        _logger = logger;

        SessionId = Guid.NewGuid();  // 세션이 생성될 때 고유 ID 할당
    }

    public void Login(long playerId, string nickname)
    {
        PlayerId = playerId;
        PlayerNickname = nickname;

        _logger.LogInformation(
            "Player logged in. SessionId={SessionId}, PlayerId={PlayerId}, Nickname={Nickname}",
            SessionId,
            playerId,
            nickname);
    }

    // 클라이언트로 바이너리 데이터를 전송하는 메서드
    public Task SendAsync(byte[] data)
    {
        if (Volatile.Read(ref _disconnected) == 1)
        {
            return Task.CompletedTask;
        }

        // 바로 전송이 아니라 데이터를 전송 큐에 삽입
        _sendQueue.Enqueue(data);

        // 이미 송신 루프가 돌고 있다면 중복 가동하지 않고 즉시 리턴
        if (Interlocked.CompareExchange(ref _sendLoopRunning, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        // 루프가 쉬고 있다면 스레드 풀에서 전송 전용 루프(SendLoopAsync)를 깨움
        _ = Task.Run(() => SendLoopAsync(_disconnectCts.Token));

        return Task.CompletedTask;
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stream = _client.GetStream();

            while (!cancellationToken.IsCancellationRequested)
            {
                while (_sendQueue.TryDequeue(out var packet))
                {
                    await stream.WriteAsync(packet, cancellationToken);
                }

                Interlocked.Exchange(ref _sendLoopRunning, 0);

                if (_sendQueue.IsEmpty)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _sendLoopRunning, 1, 0) != 0)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref _sendLoopRunning, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Send failed. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
            Interlocked.Exchange(ref _sendLoopRunning, 0);
            Disconnect();
        }
    }

    // 세션 통신 루프 시작점
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((Session)state!).Disconnect(),
            this);

        var token = _disconnectCts.Token;
        var stream = _client.GetStream();
        var buffer = new byte[ReadBufferSize];

        try
        {
            while (!token.IsCancellationRequested)
            {
                // 클라이언트로부터 데이터 수신 대기
                var length = await stream.ReadAsync(buffer, token);
                LastReceiveTime = DateTime.UtcNow;

                // Console.WriteLine($"ReadAsync Length: {length}");
                // Console.WriteLine(BitConverter.ToString(buffer, 0, length));

                if (length == 0) break; // 연결 끊으면 루프 탈출

                // 수신한 바이트 데이터를 세션 전용 수신 버퍼에 누적 저장
                _receiveBuffer.Write(buffer, length);
                // Console.WriteLine($"Buffer Length After Write: {_receiveBuffer.Length}");

                // 버퍼에 완전한 패킷이 들어올 때까지 루프 가동
                while (true)
                {
                    // 패킷 헤더 전체가 모인 뒤 size/opcode를 해석
                    if (_receiveBuffer.Length < PacketReader.HeaderSize) break;

                    // 버퍼의 맨 앞 2바이트 읽어서 패킷의 전체 크기(packetSize) 획득
                    var packetSize = BinaryPrimitives.ReadUInt16LittleEndian(
                        _receiveBuffer.Buffer.AsSpan(0, sizeof(ushort)));

                    if (packetSize < PacketReader.HeaderSize)
                    {
                        throw new PacketProtocolException(
                            $"Invalid packet size: {packetSize}. Minimum packet size is {PacketReader.HeaderSize}.");
                    }

                    if (packetSize > MaxPacketSize)
                    {
                        throw new PacketProtocolException(
                            $"Invalid packet size: {packetSize}. Maximum packet size is {MaxPacketSize}.");
                    }

                    // Console.WriteLine($"PacketSize: {packetSize}");
                    // Console.WriteLine($"CurrentBufferLength: {_receiveBuffer.Length}");
                    // Console.WriteLine(BitConverter.ToString(_receiveBuffer.Buffer, 0, _receiveBuffer.Length));

                    // 아직 패킷이 덜 도착함
                    if (_receiveBuffer.Length < packetSize) break;

                    // 순수 패킷 버퍼 생성 및 복사
                    var packetData = new byte[packetSize];
                    Array.Copy(
                        _receiveBuffer.Buffer,
                        0,
                        packetData,
                        0,
                        packetSize
                    );

                    // 패킷 데이터를 디스패처로 전달
                    var reader = new PacketReader(packetData);
                    await _dispatcher.DispatchAsync(this, reader, token);

                    // 처리가 완료된 크기만큼 수신 버퍼에서 제거 및 정렬 처리
                    _receiveBuffer.Remove(packetSize);
                }

            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Session canceled. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
        }
        catch (PacketProtocolException ex)
        {
            _logger.LogWarning(
                ex,
                "Session closed due to protocol violation. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Session receive loop failed. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
        }
        finally
        {
            // 연결이 종료되었을 때 매니저에서 제거하고 소켓 폐쇄
            _sessionManager.Remove(this);
            _logger.LogInformation(
                "Client disconnected. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
            Dispose();
        }
    }

    public void Disconnect()
    {
        if (Interlocked.Exchange(ref _disconnected, 1) == 1)
        {
            return;
        }

        try
        {
            _disconnectCts.Cancel();
            _client.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {

        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Disconnect();
        _disconnectCts.Dispose();
        _client.Dispose();
    }
}
