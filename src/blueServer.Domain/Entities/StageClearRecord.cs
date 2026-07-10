namespace blueServer.Domain.Entities;

public class StageClearRecord
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public int StageTemplateId { get; set; }
    public int ClearCount { get; set; }
    public DateTime FirstClearedAt { get; set; }
    public DateTime LastClearedAt { get; set; }
    public uint Version { get; set; }

    public Player? Player { get; set; }
    public StageTemplate? StageTemplate { get; set; }

    public static StageClearRecord Create(
        long playerId,
        int stageTemplateId,
        DateTime clearedAt)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (stageTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageTemplateId),
                stageTemplateId,
                "Stage template id must be greater than zero.");
        }

        return new StageClearRecord
        {
            PlayerId = playerId,
            StageTemplateId = stageTemplateId,
            ClearCount = 1,
            FirstClearedAt = clearedAt,
            LastClearedAt = clearedAt
        };
    }

    public void RecordClear(DateTime clearedAt)
    {
        ClearCount = checked(ClearCount + 1);
        LastClearedAt = clearedAt;
    }
}
