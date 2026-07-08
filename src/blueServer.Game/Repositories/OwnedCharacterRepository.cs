using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Repositories;

public sealed class OwnedCharacterRepository
{
    private readonly GameDbContext _db;

    public OwnedCharacterRepository(GameDbContext db)
    {
        _db = db;
    }

    public Task<List<OwnedCharacter>> GetOwnedCharacterByIdAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        return _db.OwnedCharacters
            .Where(c => c.PlayerId == playerId)
            .ToListAsync(cancellationToken);
    }

    public Task<List<OwnedCharacter>> GetByPlayerIdWithTemplateAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        return _db.OwnedCharacters
            .AsNoTracking()
            .Include(character => character.CharacterTemplate)
            .Where(character => character.PlayerId == playerId)
            .OrderBy(character => character.Id)
            .ToListAsync(cancellationToken);
    }

    public void Add(OwnedCharacter character)
    {
        _db.OwnedCharacters.Add(character);
    }
}
