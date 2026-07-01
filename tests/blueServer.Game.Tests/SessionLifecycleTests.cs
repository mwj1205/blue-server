using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace blueServer.Game.Tests;

public sealed class SessionLifecycleTests
{
    [Fact]
    public async Task StartAsync_Completes_WhenCancellationTokenIsCanceled()
    {
        using var fixture = new SessionLifecycleFixture();
        await fixture.ConnectAsync();

        fixture.StartSession();
        fixture.CancelSessionToken();

        await fixture.WaitForSessionToStopAsync();
    }

    [Fact]
    public async Task StartAsync_Completes_WhenDisconnectIsCalled()
    {
        using var fixture = new SessionLifecycleFixture();
        await fixture.ConnectAsync();

        var session = fixture.StartSession();
        session.Disconnect();

        await fixture.WaitForSessionToStopAsync();
    }

    [Fact]
    public async Task Dispose_DoesNotThrow_WhenCalledMultipleTimesAfterSessionStops()
    {
        using var fixture = new SessionLifecycleFixture();
        await fixture.ConnectAsync();

        var session = fixture.StartSession();
        session.Disconnect();
        await fixture.WaitForSessionToStopAsync();

        session.Dispose();
        session.Dispose();
        session.Disconnect();
    }

    [Fact]
    public async Task StartAsync_Completes_WhenPacketSizeIsSmallerThanHeader()
    {
        using var fixture = new SessionLifecycleFixture();
        await fixture.ConnectAsync();

        fixture.StartSession();
        await fixture.ClientStream.WriteAsync(CreatePacketHeader(2));

        await fixture.WaitForSessionToStopAsync();
    }

    [Fact]
    public async Task StartAsync_Completes_WhenPacketSizeExceedsMaxPacketSize()
    {
        using var fixture = new SessionLifecycleFixture();
        await fixture.ConnectAsync();

        fixture.StartSession();
        await fixture.ClientStream.WriteAsync(CreatePacketHeader(4097));

        await fixture.WaitForSessionToStopAsync();
    }

    private static byte[] CreatePacketHeader(ushort packetSize)
    {
        var packet = new byte[PacketReader.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, sizeof(ushort)), packetSize);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(sizeof(ushort), sizeof(ushort)), (ushort)Opcode.Ping);
        return packet;
    }

    private sealed class SessionLifecycleFixture : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _sessionCts = new(TimeSpan.FromSeconds(5));
        private readonly ServiceProvider _provider;

        private TcpClient? _serverClient;
        private Session? _session;
        private Task? _sessionTask;

        public SessionLifecycleFixture()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);

            var services = new ServiceCollection();
            _provider = services.BuildServiceProvider();

            Client = new TcpClient();
        }

        public TcpClient Client { get; }

        public NetworkStream ClientStream => Client.GetStream();

        public async Task ConnectAsync()
        {
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            var acceptTask = _listener.AcceptTcpClientAsync(_sessionCts.Token);
            await Client.ConnectAsync(IPAddress.Loopback, port, _sessionCts.Token);

            _serverClient = await acceptTask;
        }

        public Session StartSession()
        {
            if (_serverClient is null)
            {
                throw new InvalidOperationException("ConnectAsync must be called before starting a session.");
            }

            var dispatcher = new PacketDispatcher(
                _provider.GetRequiredService<IServiceScopeFactory>());

            _session = new Session(
                _serverClient,
                dispatcher,
                NullLogger<Session>.Instance);

            _sessionTask = _session.StartAsync(_sessionCts.Token);
            return _session;
        }

        public void CancelSessionToken()
        {
            _sessionCts.Cancel();
        }

        public async Task WaitForSessionToStopAsync()
        {
            if (_sessionTask is null)
            {
                return;
            }

            await _sessionTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            _session?.Dispose();
            _sessionCts.Cancel();
            Client.Dispose();
            _serverClient?.Dispose();
            _listener.Stop();
            _provider.Dispose();
            _sessionCts.Dispose();
        }
    }
}
