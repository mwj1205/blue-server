using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using Xunit;

namespace blueServer.Game.Tests.Domain;

public sealed class InventoryItemChangeLogTests
{
    [Theory]
    [InlineData(30, 100, 130)]
    [InlineData(-20, 100, 80)]
    public void Create_CalculatesQuantityAfterAndNormalizesSourceId(
        int delta,
        int quantityBefore,
        int expectedQuantityAfter)
    {
        var requestId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        var change = InventoryItemChangeLog.Create(
            1,
            1001,
            delta,
            quantityBefore,
            InventoryItemChangeReasonType.StageClearReward,
            "  stage-clear:42  ",
            requestId,
            createdAt);

        Assert.Equal(1, change.PlayerId);
        Assert.Equal(1001, change.ItemTemplateId);
        Assert.Equal(delta, change.Delta);
        Assert.Equal(quantityBefore, change.QuantityBefore);
        Assert.Equal(expectedQuantityAfter, change.QuantityAfter);
        Assert.Equal(
            InventoryItemChangeReasonType.StageClearReward,
            change.ReasonType);
        Assert.Equal("stage-clear:42", change.SourceId);
        Assert.Equal(requestId, change.RequestId);
        Assert.Equal(createdAt, change.CreatedAt);
    }

    [Fact]
    public void Create_RejectsQuantityBelowZero()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateChange(
                delta: -101,
                quantityBefore: 100));
    }

    [Fact]
    public void Create_RejectsQuantityAboveInventoryLimit()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateChange(
                delta: 1,
                quantityBefore: ItemTemplate.InventoryMaxQuantity));
    }

    [Fact]
    public void Create_RejectsZeroDelta()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateChange(
                delta: 0,
                quantityBefore: 100));
    }

    [Fact]
    public void Create_RejectsEmptyRequestId()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryItemChangeLog.Create(
                1,
                1001,
                10,
                100,
                InventoryItemChangeReasonType.RewardGrant,
                "reward:42",
                Guid.Empty,
                DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryItemChangeLog.Create(
                1,
                1001,
                10,
                100,
                InventoryItemChangeReasonType.RewardGrant,
                "reward:42",
                Guid.NewGuid(),
                DateTime.Now));
    }

    private static InventoryItemChangeLog CreateChange(
        int delta,
        int quantityBefore)
    {
        return InventoryItemChangeLog.Create(
            1,
            1001,
            delta,
            quantityBefore,
            InventoryItemChangeReasonType.RewardGrant,
            "reward:42",
            Guid.NewGuid(),
            DateTime.UtcNow);
    }
}
