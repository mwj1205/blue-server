using blueServer.Domain.Rewards;

namespace blueServer.Domain.Entities;

public sealed class RewardGrantItem
{
    public long Id { get; set; }
    public long RewardGrantRecordId { get; set; }
    public RewardType Type { get; set; }
    public int Amount { get; set; }

    public RewardGrantRecord? RewardGrantRecord { get; set; }

    public static RewardGrantItem Create(RewardType type, int amount)
    {
        var reward = RewardItem.Create(type, amount);

        return new RewardGrantItem
        {
            Type = reward.Type,
            Amount = reward.Amount
        };
    }
}
