using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Repositories;

public class PlayerRepository
{
    // GameDbContext 대신 Factory를 주입받음
    private readonly IDbContextFactory<GameDbContext> _contextFactory;

    public PlayerRepository(IDbContextFactory<GameDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // 메서드마다 새로운 DbContext를 생성해서 사용
    public async Task<Player?> FindByNicknameAsync(string nickname)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Players.FirstOrDefaultAsync(p => p.Nickname == nickname);
    }

    public async Task<Player?> FindByIdAsync(long id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Players.FirstOrDefaultAsync(p => p.Id == id);
    }
}
