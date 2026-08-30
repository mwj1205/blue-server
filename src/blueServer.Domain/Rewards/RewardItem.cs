namespace blueServer.Domain.Rewards;

public sealed record RewardItem
{
    private RewardItem(RewardType type, int amount)
    {
        Type = type;
        Amount = amount;
    }

    public RewardType Type { get; }
    public int Amount { get; }

    public static RewardItem Create(RewardType type, int amount)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Reward type is not supported.");
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Reward amount must be greater than zero.");
        }

        return new RewardItem(type, amount);
    }
}
