namespace blueServer.GrainContracts.PlayerProfiles;

[GenerateSerializer]
public sealed class PlayerProfileSnapshot
{
    [Id(0)]
    public long Id { get; init; }

    [Id(1)]
    public string Nickname { get; init; } = "";

    [Id(2)]
    public int Gold { get; init; }

    [Id(3)]
    public int Gem { get; init; }

    [Id(4)]
    public int OwnedCharacterCount { get; init; }

    [Id(5)]
    public int PartyCount { get; init; }

    [Id(6)]
    public int ClearedStageCount { get; init; }

    [Id(7)]
    public int TotalStageClearCount { get; init; }
}
