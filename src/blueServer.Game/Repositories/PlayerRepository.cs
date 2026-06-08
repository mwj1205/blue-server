using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Repositories;

public class PlayerRepository
{
    private readonly GameDbContext _db;

    public PlayerRepository(GameDbContext db)
    {
        _db = db;
    }

    public async Task<Player?> FindByNicknameAsync(string nickname)
    {
        return await _db.Players.FirstOrDefaultAsync(p => p.Nickname == nickname);
    }

    public async Task<Player?> FindByIdAsync(long id)
    {
        return await _db.Players.FirstOrDefaultAsync(p => p.Id == id);
    }
}