using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace blueServer.Game.Tests.Handlers;

public sealed class PacketDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_CallsRegisteredHandler_WhenOpcodeMatches()
    {
        var handler = new RecordingPacketHandler();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton(handler);
            services.AddKeyedSingleton<IPacketHandler>(
                Opcode.Ping,
                (serviceProvider, _) => serviceProvider.GetRequiredService<RecordingPacketHandler>());
        });
        var dispatcher = CreateDispatcher(provider);
        var session = CreateSession(dispatcher);
        var reader = new PacketReader(CreatePacket(Opcode.Ping));

        await dispatcher.DispatchAsync(session, reader);

        Assert.True(handler.WasCalled);
        Assert.Same(session, handler.Session);
        Assert.Equal(Opcode.Ping, handler.Opcode);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotCallRegisteredHandler_WhenOpcodeDoesNotMatch()
    {
        var handler = new RecordingPacketHandler();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton(handler);
            services.AddKeyedSingleton<IPacketHandler>(
                Opcode.Ping,
                (serviceProvider, _) => serviceProvider.GetRequiredService<RecordingPacketHandler>());
        });
        var dispatcher = CreateDispatcher(provider);
        var session = CreateSession(dispatcher);
        var reader = new PacketReader(CreatePacket(Opcode.Chat));

        await dispatcher.DispatchAsync(session, reader);

        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_CreatesNewScopeForEachDispatch_WhenHandlerIsScoped()
    {
        var tracker = new ScopedHandlerTracker();
        using var provider = CreateServiceProvider(services =>
        {
            services.AddSingleton(tracker);
            services.AddKeyedScoped<IPacketHandler, ScopedRecordingPacketHandler>(Opcode.Ping);
        });
        var dispatcher = CreateDispatcher(provider);
        var session = CreateSession(dispatcher);

        await dispatcher.DispatchAsync(session, new PacketReader(CreatePacket(Opcode.Ping)));
        await dispatcher.DispatchAsync(session, new PacketReader(CreatePacket(Opcode.Ping)));

        Assert.Equal(2, tracker.CreatedCount);
        Assert.Equal(2, tracker.HandledInstanceIds.Distinct().Count());
    }

    private static ServiceProvider CreateServiceProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static PacketDispatcher CreateDispatcher(IServiceProvider provider)
    {
        return new PacketDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static Session CreateSession(PacketDispatcher dispatcher)
    {
        return new Session(
            new TcpClient(),
            dispatcher,
            NullLogger<Session>.Instance);
    }

    private static byte[] CreatePacket(Opcode opcode)
    {
        var packet = new byte[PacketReader.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), (ushort)packet.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(2, 2), (ushort)opcode);
        return packet;
    }

    private sealed class RecordingPacketHandler : IPacketHandler
    {
        public bool WasCalled { get; private set; }
        public Session? Session { get; private set; }
        public Opcode? Opcode { get; private set; }

        public Task HandleAsync(Session session, PacketReader reader)
        {
            WasCalled = true;
            Session = session;
            Opcode = reader.Opcode;
            return Task.CompletedTask;
        }
    }

    private sealed class ScopedHandlerTracker
    {
        private int _createdCount;

        public int CreatedCount => _createdCount;
        public ConcurrentQueue<int> HandledInstanceIds { get; } = new();

        public int CreateInstanceId()
        {
            return Interlocked.Increment(ref _createdCount);
        }
    }

    private sealed class ScopedRecordingPacketHandler : IPacketHandler
    {
        private readonly ScopedHandlerTracker _tracker;
        private readonly int _instanceId;

        public ScopedRecordingPacketHandler(ScopedHandlerTracker tracker)
        {
            _tracker = tracker;
            _instanceId = tracker.CreateInstanceId();
        }

        public Task HandleAsync(Session session, PacketReader reader)
        {
            _tracker.HandledInstanceIds.Enqueue(_instanceId);
            return Task.CompletedTask;
        }
    }
}
