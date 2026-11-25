using blueServer.Game.Packets;
using blueServer.Infrastructure.Items;

namespace blueServer.Game.Handlers;

public static class InventoryPacketMapper
{
    public static InventoryListResultPacket ToPacket(
        InventoryListResult result,
        Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        var status = result.Status switch
        {
            InventoryListStatus.Success => InventoryListPacketStatus.Success,
            InventoryListStatus.PlayerNotFound => InventoryListPacketStatus.PlayerNotFound,
            InventoryListStatus.InventoryTooLarge => InventoryListPacketStatus.InventoryTooLarge,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status), result.Status, "Unexpected inventory list status.")
        };

        return new InventoryListResultPacket(
            requestId,
            status,
            result.Items.Select(item => new InventoryListPacketItem(
                item.ItemTemplateId,
                item.Quantity,
                item.Type,
                item.IsActive)).ToArray());
    }
}
