using blueServer.Domain.Items;
using blueServer.Infrastructure.Items;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class InventoryDecreaseRequestTests
{
    [Fact]
    public void Constructor_NormalizesDuplicateCostsAndCopiesInput()
    {
        var input = new[]
        {
            new InventoryDecreaseItem(2001, 5),
            new InventoryDecreaseItem(1001, 3),
            new InventoryDecreaseItem(1001, 7)
        };
        var request = Create(input);
        input[0] = new InventoryDecreaseItem(2001, 999);

        Assert.Equal(new[]
        {
            new InventoryDecreaseItem(1001, 10),
            new InventoryDecreaseItem(2001, 5)
        }, request.Items);
        Assert.Equal("test-consumption", request.SourceId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void Constructor_RejectsInvalidCost(int id, int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create([new(id, amount)]));
    }

    [Fact]
    public void Constructor_RejectsEmptyTooManyAndOverflowingCosts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            Enumerable.Range(1, 101).Select(id => new InventoryDecreaseItem(id, 1))));
        Assert.Throws<OverflowException>(() => Create(
            [new(1, int.MaxValue), new(1, 1)]));
    }

    [Fact]
    public void Constructor_RejectsInvalidAuditMetadata()
    {
        InventoryDecreaseItem[] items = [new(1, 1)];
        Assert.Throws<ArgumentException>(() => new InventoryDecreaseRequest(
            items, InventoryItemChangeReasonType.AdminAdjustment, "test", Guid.Empty, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new InventoryDecreaseRequest(
            items, InventoryItemChangeReasonType.AdminAdjustment, "test", Guid.NewGuid(), DateTime.MinValue));
        Assert.Throws<ArgumentException>(() => new InventoryDecreaseRequest(
            items, InventoryItemChangeReasonType.AdminAdjustment, " ", Guid.NewGuid(), DateTime.UtcNow));
    }

    private static InventoryDecreaseRequest Create(IEnumerable<InventoryDecreaseItem> items) =>
        new(items, InventoryItemChangeReasonType.AdminAdjustment,
            " test-consumption ", Guid.NewGuid(), DateTime.UtcNow);
}
