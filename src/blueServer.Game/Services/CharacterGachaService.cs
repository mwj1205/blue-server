using blueServer.Domain.Entities;
using blueServer.Game.Repositories;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Services;

public sealed class CharacterGachaService
{
    public const int GachaCost = 100;

    private readonly GameDbContext _db;
    private readonly PlayerRepository _players;
    private readonly OwnedCharacterRepository _ownedCharacters;
    private readonly CharacterTemplateRepository _characterTemplates;

    public CharacterGachaService(
        GameDbContext db,
        PlayerRepository players,
        OwnedCharacterRepository ownedCharacters,
        CharacterTemplateRepository characterTemplates)
    {
        _db = db;
        _players = players;
        _ownedCharacters = ownedCharacters;
        _characterTemplates = characterTemplates;
    }

    public async Task<CharacterGachaResult> DrawAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var player = await _players.FindByIdAsync(playerId, cancellationToken);

            if (player is null)
            {
                return CharacterGachaResult.Fail("Player not found");
            }

            var templates = await _characterTemplates.GetAllAsync(cancellationToken);

            if (templates.Count == 0)
            {
                return CharacterGachaResult.Fail("No character templates available", player.Gem);
            }

            var template = templates[Random.Shared.Next(templates.Count)];

            if (!player.TrySpendGems(GachaCost))
            {
                return CharacterGachaResult.Fail("Not enough gems", player.Gem);
            }

            var ownedCharacter = OwnedCharacter.Create(player.Id, template);
            _ownedCharacters.Add(ownedCharacter);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CharacterGachaResult.Success(
                ownedCharacter.Id,
                template.Id,
                template.Name,
                template.Rarity,
                player.Gem);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return CharacterGachaResult.Fail(
                "Another request changed the player data. Please try again.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}

public sealed record CharacterGachaResult(
    bool IsSuccess,
    string Message,
    long OwnedCharacterId,
    int CharacterTemplateId,
    string CharacterName,
    int Rarity,
    int RemainingGem)
{
    public static CharacterGachaResult Success(
        long ownedCharacterId,
        int characterTemplateId,
        string characterName,
        int rarity,
        int remainingGem)
    {
        return new CharacterGachaResult(
            true,
            "Gacha success",
            ownedCharacterId,
            characterTemplateId,
            characterName,
            rarity,
            remainingGem);
    }

    public static CharacterGachaResult Fail(string message, int remainingGem = 0)
    {
        return new CharacterGachaResult(
            false,
            message,
            0,
            0,
            string.Empty,
            0,
            remainingGem);
    }
}
