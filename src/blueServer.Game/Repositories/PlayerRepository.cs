using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Repositories;

public sealed class PlayerRepository
{
    private readonly GameDbContext _db;

    public PlayerRepository(GameDbContext db)
    {
        _db = db;
    }

    public Task<Player?> FindByNicknameAsync(string nickname)
    {
        return _db.Players.FirstOrDefaultAsync(p => p.Nickname == nickname);
    }

    public Task<Player?> FindByIdAsync(long id)
    {
        return _db.Players.FirstOrDefaultAsync(p => p.Id == id);
    }
}
