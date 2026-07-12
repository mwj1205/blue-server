using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BlueServer.Client.Network;
using BlueServer.Client.Protocol;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BlueServer.Client.Tests
{
    public sealed class GamePacketCodecTests
    {
        [Test]
        public void LoginRequest_WritesExpectedOpcodeAndAccessToken()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteString("access-token");
                packet = writer.BuildPacket(GameOpcode.Login);
            }

            var reader = new GamePacketReader(packet);

            Assert.That(reader.Opcode, Is.EqualTo(GameOpcode.Login));
            Assert.That(reader.ReadString(), Is.EqualTo("access-token"));
            Assert.That(reader.IsConsumed, Is.True);
        }

        [Test]
        public void PlayerProfileResult_ReadsExpectedPayload()
        {
            byte[] packet;

            using (var writer = new GamePacketWriter())
            {
                writer.WriteBool(true);
                writer.WriteString("Player profile loaded");
                writer.WriteLong(10);
                writer.WriteString("Sensei");
                writer.WriteInt(1200);
                writer.WriteInt(450);
                writer.WriteInt(7);
                writer.WriteInt(2);
                writer.WriteInt(3);
                writer.WriteInt(8);
                packet = writer.BuildPacket(GameOpcode.PlayerProfileResult);
            }

            var profile = GameTcpClient.ParsePlayerProfileResponse(packet);

            Assert.That(profile.Success, Is.True);
            Assert.That(profile.Message, Is.EqualTo("Player profile loaded"));
            Assert.That(profile.PlayerId, Is.EqualTo(10));
            Assert.That(profile.Nickname, Is.EqualTo("Sensei"));
            Assert.That(profile.Gold, Is.EqualTo(1200));
            Assert.That(profile.Gem, Is.EqualTo(450));
            Assert.That(profile.OwnedCharacterCount, Is.EqualTo(7));
            Assert.That(profile.PartyCount, Is.EqualTo(2));
            Assert.That(profile.ClearedStageCount, Is.EqualTo(3));
            Assert.That(profile.TotalStageClearCount, Is.EqualTo(8));
        }

        [UnityTest]
        public IEnumerator GameTcpClient_LogsInAndReadsFragmentedProfileResponse()
        {
            var testTask = RunGameTcpClientScenarioAsync();

            while (!testTask.IsCompleted)
            {
                yield return null;
            }

            if (testTask.IsFaulted)
            {
                throw testTask.Exception.InnerException;
            }
        }

        private static async Task RunGameTcpClientScenarioAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var serverTask = RunFakeServerAsync(listener);

                using (var timeoutCts = new CancellationTokenSource(
                    System.TimeSpan.FromSeconds(5)))
                using (var client = new GameTcpClient("127.0.0.1", port))
                {
                    await client.ConnectAsync(timeoutCts.Token);

                    var login = await client.LoginAsync(
                        "access-token",
                        timeoutCts.Token);

                    var profile = await client.GetPlayerProfileAsync(
                        timeoutCts.Token);

                    Assert.That(login.Success, Is.True);
                    Assert.That(profile.Success, Is.True);
                    Assert.That(profile.PlayerId, Is.EqualTo(10));
                    Assert.That(profile.Nickname, Is.EqualTo("Sensei"));
                    Assert.That(profile.TotalStageClearCount, Is.EqualTo(8));
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
            using (var acceptedClient = await listener.AcceptTcpClientAsync())
            using (var stream = acceptedClient.GetStream())
            {
                var loginRequest = new GamePacketReader(
                    await ReadPacketAsync(stream));

                Assert.That(loginRequest.Opcode, Is.EqualTo(GameOpcode.Login));
                Assert.That(loginRequest.ReadString(), Is.EqualTo("access-token"));
                loginRequest.EnsureFullyConsumed();

                byte[] loginResponse;

                using (var writer = new GamePacketWriter())
                {
                    writer.WriteBool(true);
                    writer.WriteString("Login Success");
                    loginResponse = writer.BuildPacket(GameOpcode.LoginResult);
                }

                await WriteFragmentedAsync(stream, loginResponse);

                var profileRequest = new GamePacketReader(
                    await ReadPacketAsync(stream));

                Assert.That(
                    profileRequest.Opcode,
                    Is.EqualTo(GameOpcode.PlayerProfile));
                profileRequest.EnsureFullyConsumed();

                byte[] profileResponse;

                using (var writer = new GamePacketWriter())
                {
                    writer.WriteBool(true);
                    writer.WriteString("Player profile loaded");
                    writer.WriteLong(10);
                    writer.WriteString("Sensei");
                    writer.WriteInt(1200);
                    writer.WriteInt(450);
                    writer.WriteInt(7);
                    writer.WriteInt(2);
                    writer.WriteInt(3);
                    writer.WriteInt(8);
                    profileResponse = writer.BuildPacket(
                        GameOpcode.PlayerProfileResult);
                }

                await WriteFragmentedAsync(stream, profileResponse);
            }
        }

        private static async Task<byte[]> ReadPacketAsync(
            NetworkStream stream)
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
    }
}
