namespace blueServer.Domain.Entities;

public class CharacterTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Rarity { get; set; }

    public string Role { get; set; } = string.Empty;
}
