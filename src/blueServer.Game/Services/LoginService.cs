using blueServer.Game.Repositories;

namespace blueServer.Game.Services;

public sealed class LoginService
{
    private readonly PlayerRepository _players;

    public LoginService(PlayerRepository players)
    {
        _players = players;
    }

    public async Task<LoginResult?> LoginAsync(
        string nickname,
        string password,
        CancellationToken cancellationToken)
    {
        var player = await _players.FindByNicknameAsync(nickname, cancellationToken);

        if (player is null || player.Password != password)
        {
            return null;
        }

        return new LoginResult(player.Id, player.Nickname);
    }
}

public sealed record LoginResult(long PlayerId, string Nickname);
