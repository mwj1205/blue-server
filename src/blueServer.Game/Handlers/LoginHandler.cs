using blueServer.Game.Packets;
using blueServer.Game.Services;

namespace blueServer.Game.Handlers;

public sealed class LoginHandler : IPacketHandler
{
    private readonly LoginService _loginService;

    public LoginHandler(LoginService loginService)
    {
        _loginService = loginService;
    }

    public async Task HandleAsync(Session session, PacketReader reader)
    {
        var nickname = reader.ReadString();
        var password = reader.ReadString();
        Console.WriteLine($"[Login] {nickname}");

        var loginResult = await _loginService.LoginAsync(nickname, password);

        if (loginResult is null)
        {
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

        var result = new LoginResultPacket
        {
            Success = true,
            Message = "Login Success"
        };

        await session.SendAsync(result.Serialize());
    }
}
