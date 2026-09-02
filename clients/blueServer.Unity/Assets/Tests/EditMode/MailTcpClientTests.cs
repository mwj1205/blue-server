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
    public sealed class MailTcpClientTests
    {
        private const long MailId = 71;

        private static readonly DateTime SentAt =
            new DateTime(2026, 9, 2, 6, 0, 0, DateTimeKind.Utc);

        [UnityTest]
        public IEnumerator GameTcpClient_ExchangesCompleteMailFlow()
        {
            var testTask = RunScenarioAsync();

            while (!testTask.IsCompleted)
            {
                yield return null;
            }

            if (testTask.IsFaulted)
            {
                throw testTask.Exception.InnerException;
            }
        }

        private static async Task RunScenarioAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var serverTask = RunFakeServerAsync(listener);

                using (var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(5)))
                using (var client = new GameTcpClient("127.0.0.1", port))
                {
                    await client.ConnectAsync(timeoutCts.Token);

                    var login = await client.LoginAsync(
                        "access-token",
                        timeoutCts.Token);
                    var list = await client.GetMailListAsync(
                        20,
                        null,
                        timeoutCts.Token);
                    var detail = await client.GetMailDetailAsync(
                        MailId,
                        timeoutCts.Token);
                    var read = await client.MarkMailAsReadAsync(
                        MailId,
                        timeoutCts.Token);
                    var claim = await client.ClaimMailAsync(
                        MailId,
                        timeoutCts.Token);
                    var claimAll = await client.ClaimAllMailAsync(
                        timeoutCts.Token);

                    Assert.That(login.Success, Is.True);
                    Assert.That(list.Items.Count, Is.EqualTo(1));
                    Assert.That(list.Items[0].Id, Is.EqualTo(MailId));
                    Assert.That(detail.Mail.Attachments.Count, Is.EqualTo(2));
                    Assert.That(
                        read.Status,
                        Is.EqualTo(MailReadStatus.MarkedAsRead));
                    Assert.That(
                        claim.Status,
                        Is.EqualTo(MailClaimStatus.Claimed));
                    Assert.That(claim.CurrentGold, Is.EqualTo(2000));
                    Assert.That(
                        claimAll.Status,
                        Is.EqualTo(MailClaimAllStatus.NothingToClaim));
                }

                await serverTask;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static async Task RunFakeServerAsync(TcpListener listener)
        {
            using (var client = await listener.AcceptTcpClientAsync())
            using (var stream = client.GetStream())
            {
                await HandleLoginAsync(stream);
                await HandleListAsync(stream);
                await HandleDetailAsync(stream);
                await HandleReadAsync(stream);
                await HandleClaimAsync(stream);
                await HandleClaimAllAsync(stream);
            }
        }

        private static async Task HandleLoginAsync(NetworkStream stream)
        {
            var request = await ReadRequestAsync(stream, GameOpcode.Login);

            Assert.That(request.ReadString(), Is.EqualTo("access-token"));
            request.EnsureFullyConsumed();

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteString("Login Success");
                await WriteFragmentedAsync(
                    stream,
                    writer.BuildPacket(GameOpcode.LoginResult));
            }
        }

        private static async Task HandleListAsync(NetworkStream stream)
        {
            var request = await ReadRequestAsync(stream, GameOpcode.MailList);

            Assert.That(request.ReadInt(), Is.EqualTo(20));
            Assert.That(request.ReadBool(), Is.False);
            request.EnsureFullyConsumed();

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteString("Mail list loaded");
                writer.WriteInt(1);
                writer.WriteLong(MailId);
                writer.WriteString("Maintenance reward");
                WriteTime(writer, SentAt);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(true);
                writer.WriteInt(2);
                writer.WriteBool(false);
                await WriteFragmentedAsync(
                    stream,
                    writer.BuildPacket(GameOpcode.MailListResult));
            }
        }

        private static async Task HandleDetailAsync(NetworkStream stream)
        {
            var request = await ReadRequestAsync(stream, GameOpcode.MailDetail);

            Assert.That(request.ReadLong(), Is.EqualTo(MailId));
            request.EnsureFullyConsumed();

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteString("Mail detail loaded");
                writer.WriteLong(MailId);
                writer.WriteString("Maintenance reward");
                writer.WriteString("Thank you for your patience.");
                WriteTime(writer, SentAt);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(false);
                writer.WriteBool(true);
                writer.WriteInt(2);
                writer.WriteInt(1);
                writer.WriteInt(1000);
                writer.WriteInt(2);
                writer.WriteInt(500);
                await WriteFragmentedAsync(
                    stream,
                    writer.BuildPacket(GameOpcode.MailDetailResult));
            }
        }

        private static async Task HandleReadAsync(NetworkStream stream)
        {
            var request = await ReadRequestAsync(stream, GameOpcode.MailRead);

            Assert.That(request.ReadLong(), Is.EqualTo(MailId));
            request.EnsureFullyConsumed();

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteInt((int)MailReadStatus.MarkedAsRead);
                writer.WriteString("Mail marked as read");
                writer.WriteBool(true);
                WriteTime(writer, SentAt.AddMinutes(1));
                await WriteFragmentedAsync(
                    stream,
                    writer.BuildPacket(GameOpcode.MailReadResult));
            }
        }

        private static async Task HandleClaimAsync(NetworkStream stream)
        {
            var request = await ReadRequestAsync(stream, GameOpcode.MailClaim);

            Assert.That(request.ReadLong(), Is.EqualTo(MailId));
            request.EnsureFullyConsumed();

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteInt((int)MailClaimStatus.Claimed);
                writer.WriteString("Mail rewards claimed");
                writer.WriteBool(true);
                WriteTime(writer, SentAt.AddMinutes(2));
                writer.WriteInt(2000);
                writer.WriteInt(1000);
                await WriteFragmentedAsync(
                    stream,
                    writer.BuildPacket(GameOpcode.MailClaimResult));
            }
        }

        private static async Task HandleClaimAllAsync(NetworkStream stream)
        {
            var request = await ReadRequestAsync(
                stream,
                GameOpcode.MailClaimAll);

            request.EnsureFullyConsumed();

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteInt((int)MailClaimAllStatus.NothingToClaim);
                writer.WriteString("No claimable Mail");
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(2000);
                writer.WriteInt(1000);
                writer.WriteBool(false);
                await WriteFragmentedAsync(
                    stream,
                    writer.BuildPacket(GameOpcode.MailClaimAllResult));
            }
        }

        private static async Task<GamePacketReader> ReadRequestAsync(
            NetworkStream stream,
            GameOpcode expectedOpcode)
        {
            var reader = new GamePacketReader(await ReadPacketAsync(stream));

            Assert.That(reader.Opcode, Is.EqualTo(expectedOpcode));
            return reader;
        }

        private static async Task<byte[]> ReadPacketAsync(NetworkStream stream)
        {
            var sizeBuffer = new byte[sizeof(ushort)];
            await ReadExactlyAsync(stream, sizeBuffer, 0, sizeBuffer.Length);

            var packetSize = sizeBuffer[0] | sizeBuffer[1] << 8;
            var packet = new byte[packetSize];
            packet[0] = sizeBuffer[0];
            packet[1] = sizeBuffer[1];

            await ReadExactlyAsync(
                stream,
                packet,
                sizeof(ushort),
                packetSize - sizeof(ushort));

            return packet;
        }

        private static async Task ReadExactlyAsync(
            NetworkStream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            var totalRead = 0;

            while (totalRead < count)
            {
                var read = await stream.ReadAsync(
                    buffer,
                    offset + totalRead,
                    count - totalRead);

                if (read == 0)
                {
                    throw new GameProtocolException(
                        "Fake server connection closed unexpectedly.");
                }

                totalRead += read;
            }
        }

        private static async Task WriteFragmentedAsync(
            NetworkStream stream,
            byte[] packet)
        {
            await stream.WriteAsync(packet, 0, 1);
            await Task.Yield();
            await stream.WriteAsync(packet, 1, packet.Length - 1);
        }

        private static void WriteTime(
            GamePacketWriter writer,
            DateTime value)
        {
            writer.WriteLong(
                new DateTimeOffset(value).ToUnixTimeMilliseconds());
        }
    }
}
