using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Repositories;

public class OwnedCharacterRepository
{
    private readonly IDbContextFactory<GameDbContext> _contextFactory;

    public OwnedCharacterRepository(IDbContextFactory<GameDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<OwnedCharacter>> GetOwnedCharacterAsync(long playerId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.OwnedCharacters.Where(c => c.PlayerId == playerId).ToListAsync();
    }
}
