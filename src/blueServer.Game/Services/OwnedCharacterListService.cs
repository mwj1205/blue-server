using blueServer.Game.Repositories;

namespace blueServer.Game.Services;

public sealed class OwnedCharacterListService
{
    private readonly PlayerRepository _players;
    private readonly OwnedCharacterRepository _ownedCharacters;

    public OwnedCharacterListService(
        PlayerRepository players,
        OwnedCharacterRepository ownedCharacters)
    {
        _players = players;
        _ownedCharacters = ownedCharacters;
    }

    public async Task<OwnedCharacterListResult> GetAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        var playerExists = await _players.ExistsByIdAsync(
            playerId,
            cancellationToken);

        if (!playerExists)
        {
            return OwnedCharacterListResult.Fail("Player not found");
        }

        var characters = await _ownedCharacters.GetByPlayerIdWithTemplateAsync(
            playerId,
            cancellationToken);

        var items = characters.Select(character =>
        {
            var template = character.CharacterTemplate ??
                throw new InvalidOperationException("Owned character template must be loaded.");

            return new OwnedCharacterListItem(
                character.Id,
                character.CharacterTemplateId,
                template.Name,
                template.Rarity,
                template.Role,
                character.Level,
                character.Star,
                character.Exp);
        }).ToArray();

        return OwnedCharacterListResult.Success(items);
    }
}

public sealed record OwnedCharacterListResult(
    bool IsSuccess,
    string Message,
    IReadOnlyList<OwnedCharacterListItem> Characters)
{
    public static OwnedCharacterListResult Success(
        IReadOnlyList<OwnedCharacterListItem> characters)
    {
        return new OwnedCharacterListResult(
            true,
            "Owned characters loaded",
            characters);
    }

    public static OwnedCharacterListResult Fail(string message)
    {
        return new OwnedCharacterListResult(
            false,
            message,
            []);
    }
}

public sealed record OwnedCharacterListItem(
    long Id,
    int CharacterTemplateId,
    string CharacterName,
    int Rarity,
    string Role,
    int Level,
    int Star,
    long Exp);
