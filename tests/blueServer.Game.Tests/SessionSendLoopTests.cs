using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace blueServer.Game.Tests;

public sealed class SessionSendLoopTests
{
    [Fact]
    public async Task SendAsync_WritesPacketToClient_WhenSessionIsConnected()
    {
        using var fixture = new SessionSendLoopFixture();
        await fixture.ConnectAsync();

        var session = fixture.CreateSession();
        var packet = new PongPacket().Serialize();

        await session.SendAsync(packet);

        var received = await fixture.ReadFromClientAsync(packet.Length);

        Assert.Equal(packet, received);
    }

    [Fact]
    public async Task SendAsync_WritesAllPackets_WhenMultipleSendCallsAreQueuedConcurrently()
    {
        using var fixture = new SessionSendLoopFixture();
        await fixture.ConnectAsync();

        var session = fixture.CreateSession();
        var expectedMessages = Enumerable.Range(0, 50)
            .Select(index => $"message-{index}")
            .ToArray();
        var packets = expectedMessages
            .Select(message => new ChatMessagePacket { Message = message }.Serialize())
            .ToArray();

        await Task.WhenAll(packets.Select(packet => Task.Run(() => session.SendAsync(packet))));

        var received = await fixture.ReadFromClientAsync(packets.Sum(packet => packet.Length));
        var actualMessages = ReadChatMessages(received);

        Assert.Equal(
            expectedMessages.OrderBy(message => message).ToArray(),
            actualMessages.OrderBy(message => message).ToArray());
    }

    private static IReadOnlyList<string> ReadChatMessages(byte[] data)
    {
        var messages = new List<string>();
        var offset = 0;

        while (offset < data.Length)
        {
            Assert.True(data.Length - offset >= PacketReader.HeaderSize);

            var packetSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
            var opcode = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2, 2));

            Assert.True(packetSize >= PacketReader.HeaderSize + sizeof(ushort));
            Assert.True(packetSize <= data.Length - offset);
            Assert.Equal((ushort)Opcode.ChatMessage, opcode);

            var payloadOffset = offset + PacketReader.HeaderSize;
            var stringLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(payloadOffset, 2));
            payloadOffset += sizeof(ushort);

            var message = Encoding.UTF8.GetString(data, payloadOffset, stringLength);
            payloadOffset += stringLength;

            Assert.Equal(offset + packetSize, payloadOffset);
            messages.Add(message);

            offset += packetSize;
        }

        return messages;
    }

    private sealed class SessionSendLoopFixture : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(5));
        private readonly ServiceProvider _provider;

        private TcpClient? _serverClient;
        private Session? _session;

        public SessionSendLoopFixture()
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

            var acceptTask = _listener.AcceptTcpClientAsync(_cts.Token);
            await Client.ConnectAsync(IPAddress.Loopback, port, _cts.Token);

            _serverClient = await acceptTask;
        }

        public Session CreateSession()
        {
            if (_serverClient is null)
            {
                throw new InvalidOperationException("ConnectAsync must be called before creating a session.");
            }

            var dispatcher = new PacketDispatcher(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<PacketDispatcher>.Instance);

            _session = new Session(
                _serverClient,
                dispatcher,
                NullLogger<Session>.Instance);

            return _session;
        }

        public async Task<byte[]> ReadFromClientAsync(int length)
        {
            var data = new byte[length];
            var totalRead = 0;

            while (totalRead < data.Length)
            {
                var read = await ClientStream.ReadAsync(
                    data.AsMemory(totalRead),
                    _cts.Token);

                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                totalRead += read;
            }

            return data;
        }

        public void Dispose()
        {
            _session?.Dispose();
            _cts.Cancel();
            Client.Dispose();
            _serverClient?.Dispose();
            _listener.Stop();
            _provider.Dispose();
            _cts.Dispose();
        }
    }
}
