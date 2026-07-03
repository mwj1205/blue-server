using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Channels;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public class Session : IDisposable
{
    private const int MaxPacketSize = 4096;
    private const int ReadBufferSize = 1024;
    private const int ReceiveBufferCapacity = MaxPacketSize + ReadBufferSize;
    private const int SendQueueCapacity = 256;

    private readonly TcpClient _client;
    private readonly PacketDispatcher _dispatcher;
    private readonly ILogger<Session> _logger;
    private readonly ReceiveBuffer _receiveBuffer = new(ReceiveBufferCapacity);
    private readonly CancellationTokenSource _disconnectCts = new();
    private readonly Channel<byte[]> _sendQueue = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(SendQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    private int _sendLoopRunning;
    private int _disconnected;
    private int _disposed;

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

    public async Task SendAsync(
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (Volatile.Read(ref _disconnected) == 1)
        {
            return;
        }

        EnsureSendLoopStarted();

        try
        {
            // 큐 포화 시 대기를 통한 기본 backpressure 적용
            await _sendQueue.Writer.WriteAsync(data, cancellationToken);
        }
        catch (ChannelClosedException)
        {
        }
    }

    private void EnsureSendLoopStarted()
    {
        if (Interlocked.CompareExchange(ref _sendLoopRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() => SendLoopAsync(_disconnectCts.Token));
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stream = _client.GetStream();

            await foreach (var packet in _sendQueue.Reader.ReadAllAsync(cancellationToken))
            {
                await stream.WriteAsync(packet, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (Volatile.Read(ref _disconnected) == 1)
        {
            _logger.LogDebug(
                ex,
                "Send loop stopped after disconnect. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Send failed. SessionId={SessionId}, PlayerId={PlayerId}",
                SessionId,
                PlayerId);
            Disconnect();
        }
        finally
        {
            Interlocked.Exchange(ref _sendLoopRunning, 0);
        }
    }

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
                var length = await stream.ReadAsync(buffer, token);
                LastReceiveTime = DateTime.UtcNow;

                if (length == 0)
                {
                    break;
                }

                _receiveBuffer.Write(buffer, length);

                while (true)
                {
                    if (_receiveBuffer.Length < PacketReader.HeaderSize)
                    {
                        break;
                    }

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

                    if (_receiveBuffer.Length < packetSize)
                    {
                        break;
                    }

                    var packetData = new byte[packetSize];
                    Array.Copy(
                        _receiveBuffer.Buffer,
                        0,
                        packetData,
                        0,
                        packetSize);

                    var reader = new PacketReader(packetData);
                    await _dispatcher.DispatchAsync(this, reader, token);

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
            _sendQueue.Writer.TryComplete();
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
