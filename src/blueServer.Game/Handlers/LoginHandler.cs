using blueServer.Game.Packets;
using blueServer.Game.Services;
using Microsoft.Extensions.Logging;

namespace blueServer.Game.Handlers;

public sealed class LoginHandler : IPacketHandler
{
    private readonly LoginService _loginService;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        LoginService loginService,
        ILogger<LoginHandler> logger)
    {
        _loginService = loginService;
        _logger = logger;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        var nickname = reader.ReadString();
        var password = reader.ReadString();

        _logger.LogInformation(
            "Login packet received. SessionId={SessionId}, Nickname={Nickname}",
            session.SessionId,
            nickname);

        var loginResult = await _loginService.LoginAsync(
            nickname,
            password,
            cancellationToken);

        if (loginResult is null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogWarning(
                "Login failed. SessionId={SessionId}, Nickname={Nickname}",
                session.SessionId,
                nickname);

            await session.SendAsync(
                new LoginResultPacket
                {
                    Success = false,
                    Message = "Login failed"
                }.Serialize()
            );
            return;
        }

        session.Login(loginResult.PlayerId, loginResult.Nickname);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new LoginResultPacket
        {
            Success = true,
            Message = "Login Success"
        };

        await session.SendAsync(result.Serialize());
    }
}
