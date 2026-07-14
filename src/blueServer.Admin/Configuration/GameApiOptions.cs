namespace blueServer.Admin.Configuration;

public sealed class GameApiOptions
{
    public const string SectionName = "GameApi";

    public string BaseAddress { get; set; } = "http://localhost:5201";
    public int TimeoutSeconds { get; set; } = 5;
    public bool EnableInsecurePlayerLookup { get; set; }
}
