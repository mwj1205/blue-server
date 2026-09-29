using blueServer.Domain.Entities;
using blueServer.Infrastructure.Items;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class InventoryOverflowMailRequestFactoryTests
{
    [Fact]
    public void Create_GroupsOverflowAndUsesStandardExpiration()
    {
        var sentAt = new DateTime(
            2026,
            9,
            28,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var results = new[]
        {
            CreateResult(1001, requested: 1_200, applied: 999, overflow: 201),
            CreateResult(1001, requested: 50, applied: 0, overflow: 50),
            CreateResult(2001, requested: 10, applied: 10, overflow: 0)
        };

        var request = InventoryOverflowMailRequestFactory.Create(
            7,
            results,
            CreateOptions(
                sentAt,
                InventoryOverflowMailRetentionPolicy.Standard30Days));

        Assert.NotNull(request);
        Assert.Equal(sentAt.AddDays(30), request.ExpiresAt);
        Assert.Collection(
            request.InventoryItemRewards!
                .OrderBy(reward => reward.ItemTemplateId),
            reward =>
            {
                Assert.Equal(1001, reward.ItemTemplateId);
                Assert.Equal(251, reward.Quantity);
            });
    }

    [Fact]
    public void Create_UsesNoExpirationForNeverPolicy()
    {
        var request = InventoryOverflowMailRequestFactory.Create(
            7,
            [CreateResult(1001, requested: 100, applied: 0, overflow: 100)],
            CreateOptions(
                DateTime.UtcNow,
                InventoryOverflowMailRetentionPolicy.Never));

        Assert.NotNull(request);
        Assert.Null(request.ExpiresAt);
    }

    [Fact]
    public void Create_ReturnsNullWhenNoOverflowExists()
    {
        var request = InventoryOverflowMailRequestFactory.Create(
            7,
            [CreateResult(1001, requested: 100, applied: 100, overflow: 0)],
            CreateOptions(
                DateTime.UtcNow,
                InventoryOverflowMailRetentionPolicy.Standard30Days));

        Assert.Null(request);
    }

    [Fact]
    public void Create_RejectsFailedInventoryResult()
    {
        var failed = InventoryIncreaseResult.ItemTemplateInactive(1001, 10);

        Assert.Throws<ArgumentException>(() =>
            InventoryOverflowMailRequestFactory.Create(
                7,
                [failed],
                CreateOptions(
                    DateTime.UtcNow,
                    InventoryOverflowMailRetentionPolicy.Standard30Days)));
    }

    private static InventoryIncreaseResult CreateResult(
        int itemTemplateId,
        int requested,
        int applied,
        int overflow)
    {
        return InventoryIncreaseResult.Increased(
            itemTemplateId,
            requested,
            applied,
            overflow,
            applied);
    }

    private static InventoryOverflowMailOptions CreateOptions(
        DateTime sentAt,
        InventoryOverflowMailRetentionPolicy retentionPolicy)
    {
        return new InventoryOverflowMailOptions(
            MailSourceType.System,
            "inventory-overflow:test",
            "Inventory overflow",
            "Inventory overflow reward.",
            sentAt,
            retentionPolicy);
    }
}
