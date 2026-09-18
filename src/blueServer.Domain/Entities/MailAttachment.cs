using blueServer.Domain.Rewards;

namespace blueServer.Domain.Entities;

public sealed class MailAttachment
{
    public long Id { get; set; }
    public long MailId { get; set; }
    public RewardType Type { get; set; }
    public int Amount { get; set; }

    public Mail? Mail { get; set; }

    public static MailAttachment Create(
        RewardType type,
        int amount)
    {
        var reward = CurrencyReward.Create(type, amount);

        return new MailAttachment
        {
            Type = reward.Type,
            Amount = reward.Amount
        };
    }

    public CurrencyReward ToCurrencyReward()
    {
        return CurrencyReward.Create(Type, Amount);
    }
}
