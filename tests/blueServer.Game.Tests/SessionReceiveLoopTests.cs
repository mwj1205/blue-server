using System.Net;
using System.Net.Sockets;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace blueServer.Game.Tests;

public sealed class SessionReceiveLoopTests
{
    [Fact]
    public async Task StartAsync_DispatchesPacket_WhenCompletePacketIsReceived()
    {
        var handler = new RecordingPacketHandler();
        using var fixture = CreateFixture(handler);
        await fixture.ConnectAsync();

        var packet = new PingPacket().Serialize();

        await fixture.ClientStream.WriteAsync(packet);

        var opcode = await handler.WaitForOpcodeAsync();
        fixture.Client.Close();
        await fixture.WaitForSessionToStopAsync();

        Assert.Equal(Opcode.Ping, opcode);
    }

    [Fact]
    public async Task StartAsync_WaitsForRemainingBytes_WhenPacketArrivesInFragments()
    {
        var handler = new RecordingPacketHandler();
        using var fixture = CreateFixture(handler);
        await fixture.ConnectAsync();

        var packet = new PingPacket().Serialize();
        await fixture.ClientStream.WriteAsync(packet.AsMemory(0, 2));

        Assert.False(handler.WasCalled);

        await fixture.ClientStream.WriteAsync(packet.AsMemory(2));

        var opcode = await handler.WaitForOpcodeAsync();
        fixture.Client.Close();
        await fixture.WaitForSessionToStopAsync();

        Assert.Equal(Opcode.Ping, opcode);
    }

    private static SessionTestFixture CreateFixture(RecordingPacketHandler handler)
    {
        var services = new ServiceCollection();

        services.AddSingleton(handler);
        services.AddKeyedSingleton<IPacketHandler>(
            Opcode.Ping,
            (provider, _) => provider.GetRequiredService<RecordingPacketHandler>());

        var provider = services.BuildServiceProvider();
        var dispatcher = new PacketDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>());

        return new SessionTestFixture(dispatcher);
    }

    private sealed class SessionTestFixture : IDisposable
    {
        private readonly PacketDispatcher _dispatcher;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(5));

        private Task? _sessionTask;
        private TcpClient? _serverClient;

        public SessionTestFixture(PacketDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            Client = new TcpClient();
        }

        public TcpClient Client { get; }

        public NetworkStream ClientStream => Client.GetStream();

        public async Task ConnectAsync()
        {
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            var acceptTask = _listener.AcceptTcpClientAsync(_cts.Token);
            await Client.ConnectAsync(IPAddress.Loopback, port, _cts.Token);

            _serverClient = await acceptTask;

            var session = new Session(
                _serverClient,
                _dispatcher,
                new SessionManager(NullLogger<SessionManager>.Instance),
                NullLogger<Session>.Instance);

            _sessionTask = session.StartAsync(_cts.Token);
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
            _cts.Cancel();
            Client.Dispose();
            _serverClient?.Dispose();
            _listener.Stop();
            _cts.Dispose();
        }
    }

    private sealed class RecordingPacketHandler : IPacketHandler
    {
        private readonly TaskCompletionSource<Opcode> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCalled => _completion.Task.IsCompleted;

        public Task HandleAsync(
            Session session,
            PacketReader reader,
            CancellationToken cancellationToken)
        {
            _completion.TrySetResult(reader.Opcode);
            return Task.CompletedTask;
        }

        public Task<Opcode> WaitForOpcodeAsync()
        {
            return _completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
