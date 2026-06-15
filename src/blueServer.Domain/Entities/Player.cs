namespace blueServer.Domain.Entities;

public class Player
{
    public long Id { get; set; }
    public string Password { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int Gold { get; set; }
    public int Gem { get; set; }
    public uint Version { get; set; }

    public ICollection<OwnedCharacter> OwnedCharacters { get; set; } = new List<OwnedCharacter>();
}
