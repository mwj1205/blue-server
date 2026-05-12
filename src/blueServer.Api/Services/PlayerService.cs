using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Api.Services;

public class PlayerService
{
    private readonly GameDbContext _db;

    public PlayerService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<Player> CreatePlayerAsync(Player player)
    {
        // 닉네임 중복 체크
        var exists = await _db.Players.AnyAsync(X => X.Nickname == player.Nickname); 

        if (exists)
        {
            throw new Exception("Nickname already exists.");
        }

        // 초기 재화
        player.Gold = 1000;
        player.Gem = 500;

        _db.Players.Add(player);

        await _db.SaveChangesAsync();

        return player;
    }

    public async Task<Player?> GetPlayerByIdAsync(long id)
    {
        return await _db.Players.FindAsync(id);
    }
}