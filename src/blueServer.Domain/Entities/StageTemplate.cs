namespace blueServer.Domain.Entities;

public class StageTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RewardGold { get; set; }
    public int RewardGem { get; set; }

    public ICollection<StageClearRecord> ClearRecords { get; set; } = new List<StageClearRecord>();
}
