using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Protocol;

namespace BlueServer.Client.Network
{
    public sealed class GameTcpClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

        private TcpClient _client;
        private NetworkStream _stream;
        private int _disposed;

        public GameTcpClient(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException(
                    "Host is required.",
                    nameof(host));
            }

            if (port <= 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            _host = host;
            _port = port;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_client != null)
            {
                throw new InvalidOperationException(
                    "The TCP client is already connected or connecting.");
            }

            var client = new TcpClient();

            try
            {
                await ConnectWithCancellationAsync(
                    client,
                    _host,
                    _port,
                    cancellationToken);

                _client = client;
                _stream = client.GetStream();
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        public async Task<LoginResponse> LoginAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException(
                    "Access token is required.",
                    nameof(accessToken));
            }

            byte[] request;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteString(accessToken);
                request = writer.BuildPacket(GameOpcode.Login);
            }

            var response = await ExchangeAsync(
                request,
                cancellationToken);

            var reader = new GamePacketReader(response);
            EnsureOpcode(reader, GameOpcode.LoginResult);

            var result = new LoginResponse(
                reader.ReadBool(),
                reader.ReadString());

            reader.EnsureFullyConsumed();
            return result;
        }

        public async Task<PlayerProfileResponse> GetPlayerProfileAsync(
            CancellationToken cancellationToken)
        {
            byte[] request;

            using (var writer = new GamePacketWriter())
            {
                request = writer.BuildPacket(GameOpcode.PlayerProfile);
            }

            var response = await ExchangeAsync(
                request,
                cancellationToken);

            return ParsePlayerProfileResponse(response);
        }

        public static PlayerProfileResponse ParsePlayerProfileResponse(
            byte[] response)
        {
            var reader = new GamePacketReader(response);
            EnsureOpcode(reader, GameOpcode.PlayerProfileResult);

            var result = new PlayerProfileResponse(
                reader.ReadBool(),
                reader.ReadString(),
                reader.ReadLong(),
                reader.ReadString(),
                reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt(),
                reader.ReadInt());

            reader.EnsureFullyConsumed();
            return result;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            if (_stream != null)
            {
                _stream.Dispose();
            }

            if (_client != null)
            {
                _client.Close();
            }
        }

        private async Task<byte[]> ExchangeAsync(
            byte[] request,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_stream == null)
            {
                throw new InvalidOperationException(
                    "The TCP client is not connected.");
            }

            await _requestLock.WaitAsync(cancellationToken);

            try
            {
                await _stream.WriteAsync(
                    request,
                    0,
                    request.Length,
                    cancellationToken);

                return await ReadPacketAsync(
                    _stream,
                    cancellationToken);
            }
            finally
            {
                _requestLock.Release();
            }
        }

        private static async Task<byte[]> ReadPacketAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            var sizeBuffer = new byte[sizeof(ushort)];

            await ReadExactlyAsync(
                stream,
                sizeBuffer,
                0,
                sizeBuffer.Length,
                cancellationToken);

            var packetSize = sizeBuffer[0] | sizeBuffer[1] << 8;

            if (packetSize < GamePacketReader.HeaderSize ||
                packetSize > GamePacketWriter.MaxPacketSize)
            {
                throw new GameProtocolException(
                    "Received packet size is outside the allowed range.");
            }

            var packet = new byte[packetSize];
            packet[0] = sizeBuffer[0];
            packet[1] = sizeBuffer[1];

            await ReadExactlyAsync(
                stream,
                packet,
                sizeof(ushort),
                packetSize - sizeof(ushort),
                cancellationToken);

            return packet;
        }

        private static async Task ReadExactlyAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            var totalRead = 0;

            while (totalRead < count)
            {
                var read = await stream.ReadAsync(
                    buffer,
                    offset + totalRead,
                    count - totalRead,
                    cancellationToken);

                if (read == 0)
                {
                    throw new GameProtocolException(
                        "The server closed the connection while receiving a packet.");
                }

                totalRead += read;
            }
        }

        private static async Task ConnectWithCancellationAsync(
            TcpClient client,
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connectTask = client.ConnectAsync(host, port);
            var cancellationTaskSource = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(
                () => cancellationTaskSource.TrySetResult(true)))
            {
                var completedTask = await Task.WhenAny(
                    connectTask,
                    cancellationTaskSource.Task);

                if (completedTask != connectTask)
                {
                    client.Close();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await connectTask;
            }
        }

        private static void EnsureOpcode(
            GamePacketReader reader,
            GameOpcode expectedOpcode)
        {
            if (reader.Opcode != expectedOpcode)
            {
                throw new GameProtocolException(
                    "Received an unexpected packet opcode.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(GameTcpClient));
            }
        }
    }
}
