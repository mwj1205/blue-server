using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.Mails;

namespace blueServer.Infrastructure.Items;

public static class InventoryOverflowMailRequestFactory
{
    public static MailDeliveryRequest? Create(
        long playerId,
        IReadOnlyList<InventoryIncreaseResult> inventoryResults,
        InventoryOverflowMailOptions options)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(inventoryResults);
        ArgumentNullException.ThrowIfNull(options);

        if (inventoryResults.Any(result => result is null))
        {
            throw new ArgumentException(
                "Inventory results must not contain null.",
                nameof(inventoryResults));
        }

        if (inventoryResults.Any(result => !result.IsSuccess))
        {
            throw new ArgumentException(
                "Only successful inventory results can create overflow mail.",
                nameof(inventoryResults));
        }

        ValidateOptions(options);

        var itemRewards = inventoryResults
            .Where(result => result.HasOverflow)
            .GroupBy(result => result.ItemTemplateId)
            .Select(group => InventoryItemReward.Create(
                group.Key,
                group.Aggregate(
                    0,
                    (total, result) => checked(
                        total + result.OverflowQuantity))))
            .ToArray();

        if (itemRewards.Length == 0)
        {
            return null;
        }

        return new MailDeliveryRequest(
            PlayerId: playerId,
            SourceType: options.SourceType,
            SourceId: options.SourceId,
            Title: options.Title,
            Body: options.Body,
            SentAt: options.SentAt,
            ExpiresAt: CalculateExpiresAt(options),
            Rewards: null,
            InventoryItemRewards: itemRewards);
    }

    private static DateTime? CalculateExpiresAt(
        InventoryOverflowMailOptions options)
    {
        return options.RetentionPolicy switch
        {
            InventoryOverflowMailRetentionPolicy.Standard30Days =>
                options.SentAt.AddDays(30),
            InventoryOverflowMailRetentionPolicy.Never => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.RetentionPolicy,
                "Inventory overflow mail retention policy is not supported.")
        };
    }

    private static void ValidateOptions(InventoryOverflowMailOptions options)
    {
        if (options.SentAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Inventory overflow mail time must use UTC.",
                nameof(options));
        }

        if (!Enum.IsDefined(options.RetentionPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.RetentionPolicy,
                "Inventory overflow mail retention policy is not supported.");
        }
    }
}

public enum InventoryOverflowMailRetentionPolicy
{
    Standard30Days = 1,
    Never = 2
}

public sealed record InventoryOverflowMailOptions(
    MailSourceType SourceType,
    string SourceId,
    string Title,
    string Body,
    DateTime SentAt,
    InventoryOverflowMailRetentionPolicy RetentionPolicy);
