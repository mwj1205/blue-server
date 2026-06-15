using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Repositories;

public sealed class CharacterTemplateRepository
{
    private readonly GameDbContext _db;

    public CharacterTemplateRepository(GameDbContext db)
    {
        _db = db;
    }

    public Task<List<CharacterTemplate>> GetAllAsync()
    {
        return _db.CharacterTemplates
            .AsNoTracking()
            .ToListAsync();
    }
}
