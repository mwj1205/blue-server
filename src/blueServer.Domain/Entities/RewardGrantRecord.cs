using blueServer.Domain.Rewards;

namespace blueServer.Domain.Entities;

public sealed class RewardGrantRecord
{
    public const int MaxReasonLength = 100;

    public long Id { get; set; }
    public long PlayerId { get; set; }
    public Guid RequestId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; }

    public Player? Player { get; set; }
    public ICollection<RewardGrantItem> Items { get; set; } =
        new List<RewardGrantItem>();

    public static RewardGrantRecord Create(
        long playerId,
        Guid requestId,
        string reason,
        DateTime grantedAt,
        RewardBundle rewards)
    {
        ArgumentNullException.ThrowIfNull(rewards);

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id must not be empty.",
                nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "Reward grant reason is required.",
                nameof(reason));
        }

        if (reason.Length > MaxReasonLength)
        {
            throw new ArgumentException(
                $"Reward grant reason must not exceed {MaxReasonLength} characters.",
                nameof(reason));
        }

        var record = new RewardGrantRecord
        {
            PlayerId = playerId,
            RequestId = requestId,
            Reason = reason.Trim(),
            GrantedAt = grantedAt
        };

        // 같은 RewardType을 합산하여 실제 지급 결과를 하나의 Snapshot으로 저장
        foreach (var rewardGroup in rewards.Items.GroupBy(reward => reward.Type))
        {
            var totalAmount = rewardGroup.Aggregate(
                0,
                (total, reward) => checked(total + reward.Amount));

            record.Items.Add(RewardGrantItem.Create(
                rewardGroup.Key,
                totalAmount));
        }

        return record;
    }

    public bool HasSameGrant(
        string reason,
        RewardBundle rewards)
    {
        ArgumentNullException.ThrowIfNull(rewards);

        if (!string.Equals(
                Reason,
                reason.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }

        var requestedItems = rewards.Items
            .GroupBy(reward => reward.Type)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(
                    0,
                    (total, reward) => checked(total + reward.Amount)));

        return Items.Count == requestedItems.Count &&
            Items.All(item =>
                requestedItems.TryGetValue(item.Type, out var amount) &&
                amount == item.Amount);
    }
}
