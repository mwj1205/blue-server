using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
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
    private readonly ILogger<Session> _logger;
    private readonly ReceiveBuffer _receiveBuffer = new(ReceiveBufferCapacity);
    private readonly CancellationTokenSource _disconnectCts = new();

    // 전송 대기 패킷 저장
    private readonly ConcurrentQueue<byte[]> _sendQueue = new();
    private int _sendLoopRunning;
    private int _disconnected;
    private int _disposed;

    // 세션 고유 ID
    public Guid SessionId { get; }
    public long? PlayerId { get; private set; }
    public string? PlayerNickname { get; private set; }
    public bool IsAuthenticated => PlayerId.HasValue;
    public DateTime LastReceiveTime { get; private set; } = DateTime.UtcNow;

    public Session(
        TcpClient client,
        PacketDispatcher dispatcher,
        ILogger<Session> logger)
    {
        _client = client;
        _dispatcher = dispatcher;
        _logger = logger;

        SessionId = Guid.NewGuid();
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

    // 클라이언트로 바이너리 데이터 전송
    public Task SendAsync(byte[] data)
    {
        if (Volatile.Read(ref _disconnected) == 1)
        {
            return Task.CompletedTask;
        }

        // 전송 큐에 패킷 추가
        _sendQueue.Enqueue(data);

        // 이미 송신 루프가 실행 중이면 중복 실행 방지
        if (Interlocked.CompareExchange(ref _sendLoopRunning, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        // 송신 루프 시작
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

    // 세션 수신 루프 시작
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
                // 클라이언트 데이터 수신
                var length = await stream.ReadAsync(buffer, token);
                LastReceiveTime = DateTime.UtcNow;

                if (length == 0)
                {
                    break;
                }

                // 수신 버퍼에 읽은 데이터 누적
                _receiveBuffer.Write(buffer, length);

                // 완성된 패킷이 남아 있는 동안 반복 처리
                while (true)
                {
                    // 패킷 헤더 수신 대기
                    if (_receiveBuffer.Length < PacketReader.HeaderSize)
                    {
                        break;
                    }

                    // 패킷 전체 크기 읽기
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

                    // 패킷 본문 수신 대기
                    if (_receiveBuffer.Length < packetSize)
                    {
                        break;
                    }

                    // 완성된 패킷 복사
                    var packetData = new byte[packetSize];
                    Array.Copy(
                        _receiveBuffer.Buffer,
                        0,
                        packetData,
                        0,
                        packetSize);

                    // 패킷 디스패처 전달
                    var reader = new PacketReader(packetData);
                    await _dispatcher.DispatchAsync(this, reader, token);

                    // 처리 완료 패킷 제거
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
            // 실제 리소스 해제는 세션 실행 생명주기 경계에서 처리
            _logger.LogInformation(
                "Client disconnected. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
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
