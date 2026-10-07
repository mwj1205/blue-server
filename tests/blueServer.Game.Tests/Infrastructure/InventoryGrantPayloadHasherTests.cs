using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure.Items;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class InventoryGrantPayloadHasherTests
{
    [Fact]
    public void Compute_ReturnsSameHash_WhenItemOrderChanges()
    {
        var requestId = Guid.NewGuid();
        var changedAt = DateTime.UtcNow;
        var first = CreateRequest(
            requestId,
            changedAt,
            [
                CreateItem(1001, 10, requestId, changedAt),
                CreateItem(2001, 20, requestId, changedAt)
            ]);
        var reordered = CreateRequest(
            requestId,
            changedAt,
            [
                CreateItem(2001, 20, requestId, changedAt),
                CreateItem(1001, 10, requestId, changedAt)
            ]);

        Assert.Equal(
            InventoryGrantPayloadHasher.Compute(first),
            InventoryGrantPayloadHasher.Compute(reordered));
    }

    [Fact]
    public void Compute_ReturnsDifferentHash_WhenRequestedAmountChanges()
    {
        var requestId = Guid.NewGuid();
        var changedAt = DateTime.UtcNow;
        var original = CreateRequest(
            requestId,
            changedAt,
            [CreateItem(1001, 10, requestId, changedAt)]);
        var changed = CreateRequest(
            requestId,
            changedAt,
            [CreateItem(1001, 11, requestId, changedAt)]);

        Assert.NotEqual(
            InventoryGrantPayloadHasher.Compute(original),
            InventoryGrantPayloadHasher.Compute(changed));
    }

    private static InventoryGrantRequest CreateRequest(
        Guid requestId,
        DateTime changedAt,
        IReadOnlyList<InventoryIncreaseRequest> items)
    {
        return new InventoryGrantRequest(
            items,
            new InventoryOverflowMailOptions(
                MailSourceType.System,
                $"inventory:{requestId:N}",
                "Inventory overflow",
                "Overflow items.",
                changedAt,
                InventoryOverflowMailRetentionPolicy.Standard30Days));
    }

    private static InventoryIncreaseRequest CreateItem(
        int itemTemplateId,
        int amount,
        Guid requestId,
        DateTime changedAt)
    {
        return new InventoryIncreaseRequest(
            itemTemplateId,
            amount,
            InventoryItemChangeReasonType.AdminAdjustment,
            $"inventory:{requestId:N}",
            requestId,
            changedAt);
    }
}
