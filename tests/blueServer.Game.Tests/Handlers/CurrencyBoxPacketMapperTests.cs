using blueServer.Domain.Currencies;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using blueServer.Infrastructure.Items;
using Xunit;

namespace blueServer.Game.Tests.Handlers;

public sealed class CurrencyBoxPacketMapperTests
{
    [Fact]
    public void ToPacket_PreservesCompletedSnapshot()
    {
        var id = Guid.NewGuid();
        var snapshot = Snapshot(id);
        var result = CurrencyBoxPacketMapper.ToPacket(new(CurrencyBoxUseStatus.Used, snapshot), id);

        Assert.Equal(CurrencyBoxUsePacketStatus.Used, result.Status);
        Assert.Equal(id, result.RequestId);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(snapshot.ItemTemplateId, result.Snapshot.ItemTemplateId);
        Assert.Equal(snapshot.UsedQuantity, result.Snapshot.UsedQuantity);
        Assert.Equal(snapshot.QuantityAfter, result.Snapshot.QuantityAfter);
        Assert.Equal(snapshot.CurrencyType, result.Snapshot.CurrencyType);
        Assert.Equal(snapshot.GrantedAmount, result.Snapshot.GrantedAmount);
        Assert.Equal(snapshot.CurrentGold, result.Snapshot.CurrentGold);
        Assert.Equal(snapshot.CurrentGem, result.Snapshot.CurrentGem);
        Assert.Equal(snapshot.ProcessedAt, result.Snapshot.ProcessedAt);
        Assert.Equal(83, result.Serialize().Length);
    }

    [Theory]
    [InlineData(CurrencyBoxUseStatus.PlayerNotFound, CurrencyBoxUsePacketStatus.PlayerNotFound)]
    [InlineData(CurrencyBoxUseStatus.BoxNotFound, CurrencyBoxUsePacketStatus.BoxNotFound)]
    [InlineData(CurrencyBoxUseStatus.InsufficientQuantity, CurrencyBoxUsePacketStatus.InsufficientQuantity)]
    [InlineData(CurrencyBoxUseStatus.ItemTemplateNotFound, CurrencyBoxUsePacketStatus.ItemTemplateNotFound)]
    [InlineData(CurrencyBoxUseStatus.ItemTemplateInactive, CurrencyBoxUsePacketStatus.ItemTemplateInactive)]
    [InlineData(CurrencyBoxUseStatus.IdempotencyConflict, CurrencyBoxUsePacketStatus.IdempotencyConflict)]
    [InlineData(CurrencyBoxUseStatus.ConcurrencyConflict, CurrencyBoxUsePacketStatus.ConcurrencyConflict)]
    public void ToPacket_MapsFailureWithoutSnapshot(CurrencyBoxUseStatus source, CurrencyBoxUsePacketStatus expected)
    {
        var id = Guid.NewGuid();
        var result = CurrencyBoxPacketMapper.ToPacket(new(source, null), id);
        Assert.Equal(expected, result.Status);
        Assert.Equal(id, result.RequestId);
        Assert.Null(result.Snapshot);
        Assert.Equal(47, result.Serialize().Length);
    }

    [Fact]
    public void ToPacket_RejectsInconsistentResult()
    {
        var id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => CurrencyBoxPacketMapper.ToPacket(
            new(CurrencyBoxUseStatus.Used, null), id));
        Assert.Throws<InvalidOperationException>(() => CurrencyBoxPacketMapper.ToPacket(
            new(CurrencyBoxUseStatus.BoxNotFound, Snapshot(id)), id));
        Assert.Throws<InvalidOperationException>(() => CurrencyBoxPacketMapper.ToPacket(
            new(CurrencyBoxUseStatus.Used, Snapshot(Guid.NewGuid())), id));
        Assert.Throws<ArgumentException>(() => CurrencyBoxPacketMapper.ToPacket(
            new(CurrencyBoxUseStatus.BoxNotFound, null), Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => CurrencyBoxPacketMapper.ToPacket(
            new((CurrencyBoxUseStatus)999, null), id));
    }

    private static CurrencyBoxUseSnapshot Snapshot(Guid id) =>
        new(id, 3104, 2, 8, CurrencyType.Gem, 220, 1000, 720, DateTime.UtcNow);
}
