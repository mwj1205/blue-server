using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Models;
using BlueServer.Client.Network;
using BlueServer.Client.Protocol;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BlueServer.Client.Tests
{
    public sealed class MailClaimAllItemCodecTests
    {
        [Test]
        public void Request_ContainsStableId()
        {
            var id = Guid.NewGuid();
            var reader = new GamePacketReader(MailClaimAllItemCodec.BuildRequest(id));
            Assert.That(reader.Opcode, Is.EqualTo(GameOpcode.MailClaimAll));
            Assert.That(reader.ReadString(), Is.EqualTo(id.ToString("D")));
            reader.EnsureFullyConsumed();
            Assert.Throws<ArgumentException>(() => MailClaimAllItemCodec.BuildRequest(Guid.Empty));
        }

        [Test]
        public void Assembler_CombinesTwoFramesAndRejectsAdditionalFrame()
        {
            var id = Guid.NewGuid();
            var payload = Payload(501);
            using (var assembler = new MailClaimAllItemAssembler(id))
            {
                ItemMailClaimAllResponse response;
                Assert.That(assembler.Append(Frame(id, 0, 2, payload, 0, 4046), out response), Is.False);
                Assert.That(assembler.Append(Frame(id, 1, 2, payload, 4046, payload.Length - 4046), out response), Is.True);
                Assert.That(response.Items.Count, Is.EqualTo(501));
                Assert.That(response.Items[500].GrantedQuantity, Is.EqualTo(9));
                Assert.That(response.PartiallyClaimedMailCount, Is.EqualTo(1));
                Assert.Throws<GameProtocolException>(() => assembler.Append(Frame(id, 1, 2, payload, 4046, 8), out response));
            }
        }

        [Test]
        public void Assembler_RejectsWrongIdSequenceAndUnboundedPartCount()
        {
            var id = Guid.NewGuid();
            var payload = Payload(0);
            using (var assembler = new MailClaimAllItemAssembler(id))
            {
                ItemMailClaimAllResponse response;
                Assert.Throws<GameProtocolException>(() => assembler.Append(Frame(Guid.NewGuid(), 0, 1, payload, 0, payload.Length), out response));
                Assert.Throws<GameProtocolException>(() => assembler.Append(Frame(id, 1, 2, payload, 0, payload.Length), out response));
                Assert.Throws<GameProtocolException>(() => assembler.Append(Frame(id, 0, 65, payload, 0, payload.Length), out response));
            }
        }

        [UnityTest]
        public IEnumerator TcpClient_DrainsAllPartsBeforeSendingNextRequest()
        {
            var task = RunClientAsync();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
            if (task.IsCanceled) Assert.Fail("TCP fake server scenario timed out.");
        }

        private static async Task RunClientAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                using (var client = new GameTcpClient("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port))
                {
                    var ct = timeout.Token;
                    var server = FakeServerAsync(listener, ct);
                    await client.ConnectAsync(ct);
                    var batch = client.ClaimAllMailItemsAsync(Guid.NewGuid(), ct);
                    var profile = client.GetPlayerProfileAsync(ct);
                    Assert.That((await batch).Items.Count, Is.EqualTo(501));
                    Assert.That((await profile).Gold, Is.EqualTo(1000));
                    await server;
                }
            }
            finally { listener.Stop(); }
        }

        private static async Task FakeServerAsync(TcpListener listener, CancellationToken ct)
        {
            using (var peer = await listener.AcceptTcpClientAsync())
            using (var stream = peer.GetStream())
            {
                var request = new GamePacketReader(await ReadAsync(stream, ct));
                Assert.That(request.Opcode, Is.EqualTo(GameOpcode.MailClaimAll));
                var id = Guid.Parse(request.ReadString());
                request.EnsureFullyConsumed();
                var payload = Payload(501);
                var first = Frame(id, 0, 2, payload, 0, 4046);
                await stream.WriteAsync(first, 0, 3, ct);
                await stream.WriteAsync(first, 3, first.Length - 3, ct);
                var last = Frame(id, 1, 2, payload, 4046, payload.Length - 4046);
                await stream.WriteAsync(last, 0, last.Length, ct);
                var profile = new GamePacketReader(await ReadAsync(stream, ct));
                Assert.That(profile.Opcode, Is.EqualTo(GameOpcode.PlayerProfile));
                profile.EnsureFullyConsumed();
                using (var writer = new GamePacketWriter())
                {
                    writer.WriteBool(true); writer.WriteString("ok"); writer.WriteLong(1); writer.WriteString("test");
                    writer.WriteInt(1000); writer.WriteInt(500);
                    for (var i = 0; i < 4; i++) writer.WriteInt(0);
                    var bytes = writer.BuildPacket(GameOpcode.PlayerProfileResult);
                    await stream.WriteAsync(bytes, 0, bytes.Length, ct);
                }
            }
        }

        private static async Task<byte[]> ReadAsync(NetworkStream stream, CancellationToken ct)
        {
            var header = new byte[2];
            await FillAsync(stream, header, 0, 2, ct);
            var size = header[0] | header[1] << 8;
            Assert.That(size, Is.InRange(4, 4096));
            var bytes = new byte[size];
            Buffer.BlockCopy(header, 0, bytes, 0, 2);
            await FillAsync(stream, bytes, 2, size - 2, ct);
            return bytes;
        }

        private static async Task FillAsync(NetworkStream stream, byte[] bytes, int offset, int count, CancellationToken ct)
        {
            while (count > 0)
            {
                var read = await stream.ReadAsync(bytes, offset, count, ct);
                if (read == 0) throw new GameProtocolException("Fake server connection closed.");
                offset += read; count -= read;
            }
        }

        private static byte[] Payload(int count)
        {
            using (var writer = new GamePacketWriter())
            {
                writer.WriteInt(5); writer.WriteBool(true); writer.WriteLong(0);
                writer.WriteInt(0); writer.WriteInt(0); writer.WriteInt(0);
                writer.WriteInt(1000); writer.WriteInt(500); writer.WriteBool(false);
                writer.WriteInt(1); writer.WriteInt(0); writer.WriteInt(count);
                for (var i = 0; i < count; i++) { writer.WriteInt(i + 1); writer.WriteInt(9); }
                var packet = writer.BuildPacket(GameOpcode.MailClaimAllResult);
                var bytes = new byte[packet.Length - 4];
                Buffer.BlockCopy(packet, 4, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        private static byte[] Frame(Guid id, int index, int count, byte[] payload, int offset, int length)
        {
            using (var writer = new GamePacketWriter())
            {
                writer.WriteString(id.ToString("D")); writer.WriteInt(index); writer.WriteInt(count);
                var header = writer.BuildPacket(GameOpcode.MailClaimAllResult);
                var frame = new byte[header.Length + length];
                Buffer.BlockCopy(header, 0, frame, 0, header.Length);
                frame[0] = (byte)frame.Length; frame[1] = (byte)(frame.Length >> 8);
                Buffer.BlockCopy(payload, offset, frame, header.Length, length);
                return frame;
            }
        }
    }
}
