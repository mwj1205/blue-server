namespace blueServer.Game.Services;

public interface IPlayerProfileService
{
    Task<PlayerProfileResult> GetAsync(
        long playerId,
        CancellationToken cancellationToken);
}

public sealed record PlayerProfileResult(
    bool IsSuccess,
    string Message,
    long PlayerId,
    string Nickname,
    int Gold,
    int Gem,
    int OwnedCharacterCount,
    int PartyCount,
    int ClearedStageCount,
    int TotalStageClearCount)
{
    public static PlayerProfileResult Fail(string message)
    {
        return new PlayerProfileResult(
            false,
            message,
            0,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0);
    }
}
