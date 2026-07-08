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

    public Task<Player?> FindByNicknameAsync(
        string nickname,
        CancellationToken cancellationToken = default)
    {
        return _db.Players.FirstOrDefaultAsync(
            p => p.Nickname == nickname,
            cancellationToken);
    }

    public Task<Player?> FindByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return _db.Players.FirstOrDefaultAsync(
            p => p.Id == id,
            cancellationToken);
    }

    public Task<bool> ExistsByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return _db.Players
            .AsNoTracking()
            .AnyAsync(
                player => player.Id == id,
                cancellationToken);
    }
}
