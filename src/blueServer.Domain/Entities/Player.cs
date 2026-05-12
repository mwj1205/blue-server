namespace blueServer.Domain.Entities;

public class Player
{
    public int Id { get; set; }
    public string Nickname { get; set; } = "";
    public int Gold { get; set; }
    public int Gem { get; set; }
}