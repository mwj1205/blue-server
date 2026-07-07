using blueServer.Game.Packets;
using blueServer.Game.Services;
using Microsoft.Extensions.Logging;

namespace blueServer.Game.Handlers;

public sealed class LoginHandler : IPacketHandler
{
    private readonly GameJwtValidator _jwtValidator;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        GameJwtValidator jwtValidator,
        ILogger<LoginHandler> logger)
    {
        _jwtValidator = jwtValidator;
        _logger = logger;
    }

    public async Task HandleAsync(
        Session session,
        PacketReader reader,
        CancellationToken cancellationToken)
    {
        var request = LoginRequestPacket.Read(reader);

        _logger.LogInformation(
            "Login packet received. SessionId={SessionId}",
            session.SessionId);

        var validationResult = _jwtValidator.Validate(request.AccessToken);

        if (!validationResult.IsSuccess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogWarning(
                "Login failed. SessionId={SessionId}, Reason={Reason}",
                session.SessionId,
                validationResult.ErrorMessage);

            await session.SendAsync(
                new LoginResultPacket
                {
                    Success = false,
                    Message = "Login failed"
                }.Serialize(),
                cancellationToken);
            return;
        }

        session.Login(validationResult.PlayerId, validationResult.Nickname);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new LoginResultPacket
        {
            Success = true,
            Message = "Login Success"
        };

        await session.SendAsync(result.Serialize(), cancellationToken);
    }
}
