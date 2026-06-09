using System.Net.Http.Headers;
using blueServer.Domain.Entities;
using blueServer.Game.Packets;
using blueServer.Game.Repositories;

namespace blueServer.Game.Handlers;

public class LoginHandler : IPacketHandler
{
    // PlayerRepository만 의존
    private readonly PlayerRepository _repository;

    public LoginHandler(PlayerRepository repository)
    {
        _repository = repository;
    }

    public Opcode Opcode => Opcode.Login;

    public async Task HandleAsync(Session session, PacketReader reader)
    {
        var nickname = reader.ReadString();
        var password = reader.ReadString();
        Console.WriteLine($"[Login] {nickname}");
        Console.WriteLine($"[password] {password}");

        // Repository를 통해 DB 접근
        // (Repository 내부에서 DbContext를 메서드별로 생성/관리)
        var player = await _repository.FindByNicknameAsync(nickname);

        if (player == null)
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

        if (player.Password != password)
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

        session.Login(player);

        var result = new LoginResultPacket
        {
            Success = true,
            Message = "Login Success"
        };

        await session.SendAsync(result.Serialize());
    }
}
