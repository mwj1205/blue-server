namespace blueServer.Api.DTOs;

public class OwnedCharacterResponse
{
    public long Id { get; set; }
    public int CharacterTemplateId { get; set; }
    public string CharacterName { get; set; } = "";
    public int Rarity { get; set; }
    public string Role { get; set; } = "";
    public int Level { get; set; }
    public int Star { get; set; }
    public long Exp { get; set; }
}
