using blueServer.Domain.Items;
using blueServer.Game.Handlers;
using blueServer.Game.Packets;
using blueServer.Infrastructure.Items;
using Xunit;

namespace blueServer.Game.Tests.Handlers;

public sealed class InventoryPacketMapperTests
{
    [Fact]
    public void ToPacket_PreservesRequestIdAndAllItemFields()
    {
        var requestId = Guid.NewGuid();
        var result = new InventoryListResult(InventoryListStatus.Success,
        [
            new(1001, 42, ItemType.Material, true),
            new(3101, 2, ItemType.Consumable, false)
        ]);

        var packet = InventoryPacketMapper.ToPacket(result, requestId);

        Assert.Equal(requestId, packet.RequestId);
        Assert.Equal(InventoryListPacketStatus.Success, packet.Status);
        Assert.Equal(new InventoryListPacketItem[]
        {
            new(1001, 42, ItemType.Material, true),
            new(3101, 2, ItemType.Consumable, false)
        }, packet.Items);
        Assert.Single(packet.SerializeFrames());
    }

    [Theory]
    [InlineData(InventoryListStatus.Success, InventoryListPacketStatus.Success)]
    [InlineData(InventoryListStatus.PlayerNotFound, InventoryListPacketStatus.PlayerNotFound)]
    [InlineData(InventoryListStatus.InventoryTooLarge, InventoryListPacketStatus.InventoryTooLarge)]
    public void ToPacket_MapsEmptyResultStatus(InventoryListStatus source, InventoryListPacketStatus expected)
    {
        var packet = InventoryPacketMapper.ToPacket(new(source, []), Guid.NewGuid());
        Assert.Equal(expected, packet.Status);
        Assert.Empty(packet.Items);
        Assert.Single(packet.SerializeFrames());
    }

    [Fact]
    public void ToPacket_RejectsUnknownStatusAndEmptyRequestId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryPacketMapper.ToPacket(
            new((InventoryListStatus)999, []), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => InventoryPacketMapper.ToPacket(
            new(InventoryListStatus.Success, []), Guid.Empty));
    }
}
