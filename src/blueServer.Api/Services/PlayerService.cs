using blueServer.Api.DTOs;
using blueServer.Api.Exceptions;
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

    public async Task<PlayerResponse> CreatePlayerAsync(
        CreatePlayerRequest request)
    {
        var exists = await _db.Players
            .AnyAsync(x => x.Nickname == request.Nickname);

        if (exists)
        {
            throw new GameException(
                "Nickname already exists");
        }

        var player = Player.Create(request.Nickname);

        _db.Players.Add(player);

        await _db.SaveChangesAsync();

        return new PlayerResponse
        {
            Id = player.Id,
            Nickname = player.Nickname,
            Gold = player.Gold,
            Gem = player.Gem
        };
    }

    public async Task<PlayerResponse?> GetPlayerAsync(
        long id,
        CancellationToken cancellationToken)
    {
        return await _db.Players
            .AsNoTracking()
            .Where(player => player.Id == id)
            .Select(player => new PlayerResponse
            {
                Id = player.Id,
                Nickname = player.Nickname,
                Gold = player.Gold,
                Gem = player.Gem
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OwnedCharacterResponse>?> GetOwnedCharactersAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        var playerExists = await _db.Players
            .AsNoTracking()
            .AnyAsync(
                player => player.Id == playerId,
                cancellationToken);

        if (!playerExists)
        {
            return null;
        }

        return await _db.OwnedCharacters
            .AsNoTracking()
            .Where(character => character.PlayerId == playerId)
            .OrderBy(character => character.Id)
            .Select(character => new OwnedCharacterResponse
            {
                Id = character.Id,
                CharacterTemplateId = character.CharacterTemplateId,
                CharacterName = character.CharacterTemplate!.Name,
                Rarity = character.CharacterTemplate.Rarity,
                Role = character.CharacterTemplate.Role,
                Level = character.Level,
                Star = character.Star,
                Exp = character.Exp
            })
            .ToListAsync(cancellationToken);
    }
}
