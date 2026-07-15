namespace blueServer.Admin.Models;

public sealed record PlayerDetails(
    PlayerSummary Profile,
    IReadOnlyList<OwnedCharacterSummary> Roster);

public sealed class PlayerSummary
{
    public long Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public int Gold { get; set; }
    public int Gem { get; set; }
}

public sealed class OwnedCharacterSummary
{
    public long Id { get; set; }
    public int CharacterTemplateId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public int Rarity { get; set; }
    public string Role { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Star { get; set; }
    public long Exp { get; set; }
}
