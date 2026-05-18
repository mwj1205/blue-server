namespace blueServer.Api.DTOs;

public class PlayerResponse
{
    public long Id { get; set; }
    public string Nickname { get; set; } = "";
    public int Gold { get; set; }
    public int Gem { get; set; }
}
