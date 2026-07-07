using blueServer.Game.Repositories;
using blueServer.Infrastructure.Security;

namespace blueServer.Game.Services;

public sealed class LoginService
{
    private readonly PlayerRepository _players;
    private readonly PasswordService _passwordService;

    public LoginService(
        PlayerRepository players,
        PasswordService passwordService)
    {
        _players = players;
        _passwordService = passwordService;
    }

    public async Task<LoginResult?> LoginAsync(
        string nickname,
        string password,
        CancellationToken cancellationToken)
    {
        var player = await _players.FindByNicknameAsync(nickname, cancellationToken);

        if (player is null ||
            !_passwordService.VerifyPassword(password, player.Password))
        {
            return null;
        }

        return new LoginResult(player.Id, player.Nickname);
    }
}

public sealed record LoginResult(long PlayerId, string Nickname);
