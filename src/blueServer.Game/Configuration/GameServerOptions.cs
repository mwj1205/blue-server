namespace blueServer.Game.Configuration;

public sealed class GameServerOptions
{
    public const string SectionName = "GameServer";

    public int Port { get; set; } = 7777;
}
