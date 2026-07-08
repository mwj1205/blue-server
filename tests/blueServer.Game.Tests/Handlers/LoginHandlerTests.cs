using System.Buffers.Binary;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using blueServer.Game.Services;
using blueServer.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace blueServer.Game.Tests.Handlers;

public sealed class LoginHandlerTests
{
    [Fact]
    public async Task HandleAsync_AuthenticatesSession_WhenAccessTokenIsValid()
    {
        var jwtOptions = CreateJwtOptions();
        using var fixture = new LoginHandlerFixture();
        await fixture.ConnectAsync();

        var session = fixture.CreateSession();
        var handler = new LoginHandler(
            new GameJwtValidator(Options.Create(jwtOptions)),
            NullLogger<LoginHandler>.Instance);
        var reader = new PacketReader(new LoginRequestPacket
        {
            AccessToken = CreateToken(jwtOptions, 10, "sensei")
        }.Serialize());

        await handler.HandleAsync(session, reader, fixture.CancellationToken);

        var response = new PacketReader(await fixture.ReadPacketFromClientAsync());

        Assert.True(session.IsAuthenticated);
        Assert.Equal(10, session.PlayerId);
        Assert.Equal("sensei", session.PlayerNickname);
        Assert.Equal(Opcode.LoginResult, response.Opcode);
        Assert.True(response.ReadBool());
        Assert.Equal("Login Success", response.ReadString());
    }

    [Fact]
    public async Task HandleAsync_DoesNotAuthenticateSession_WhenAccessTokenIsInvalid()
    {
        using var fixture = new LoginHandlerFixture();
        await fixture.ConnectAsync();

        var session = fixture.CreateSession();
        var handler = new LoginHandler(
            new GameJwtValidator(Options.Create(CreateJwtOptions())),
            NullLogger<LoginHandler>.Instance);
        var reader = new PacketReader(new LoginRequestPacket
        {
            AccessToken = "invalid-token"
        }.Serialize());

        await handler.HandleAsync(session, reader, fixture.CancellationToken);

        var response = new PacketReader(await fixture.ReadPacketFromClientAsync());

        Assert.False(session.IsAuthenticated);
        Assert.Null(session.PlayerId);
        Assert.Equal(Opcode.LoginResult, response.Opcode);
        Assert.False(response.ReadBool());
        Assert.Equal("Login failed", response.ReadString());
    }

    private static JwtOptions CreateJwtOptions()
    {
        return new JwtOptions
        {
            Key = "01234567890123456789012345678901",
            Issuer = "blue-server",
            Audience = "blue-game",
            AccessTokenDays = 7
        };
    }

    private static string CreateToken(
        JwtOptions options,
        long playerId,
        string nickname)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.EffectiveAudience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, playerId.ToString()),
                new Claim(ClaimTypes.Name, nickname)
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class LoginHandlerFixture : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(5));
        private readonly ServiceProvider _provider;

        private TcpClient? _serverClient;
        private Session? _session;

        public LoginHandlerFixture()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _provider = new ServiceCollection().BuildServiceProvider();
            Client = new TcpClient();
        }

        public TcpClient Client { get; }
        public CancellationToken CancellationToken => _cts.Token;

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

        public async Task<byte[]> ReadPacketFromClientAsync()
        {
            var header = await ReadExactAsync(PacketReader.HeaderSize);
            var packetSize = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0, 2));
            var packet = new byte[packetSize];

            header.CopyTo(packet.AsSpan(0, header.Length));

            var payloadLength = packetSize - header.Length;
            if (payloadLength > 0)
            {
                var payload = await ReadExactAsync(payloadLength);
                payload.CopyTo(packet.AsSpan(header.Length));
            }

            return packet;
        }

        private async Task<byte[]> ReadExactAsync(int length)
        {
            var data = new byte[length];
            var totalRead = 0;

            while (totalRead < data.Length)
            {
                var read = await Client.GetStream().ReadAsync(
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
