namespace blueServer.Api.DTOs;

public class PlayerProfileResponse
{
    public long Id { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public int Gold { get; set; }
    public int Gem { get; set; }
    public int OwnedCharacterCount { get; set; }
    public int PartyCount { get; set; }
    public int ClearedStageCount { get; set; }
    public int TotalStageClearCount { get; set; }
}
