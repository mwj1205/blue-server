using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure.Items;

namespace blueServer.Game.Packets;

public enum InventoryListPacketStatus
{
    Success = 0,
    PlayerNotFound = 1,
    InventoryTooLarge = 2
}

public sealed record InventoryListPacketItem(
    int ItemTemplateId,
    int Quantity,
    ItemType Type,
    bool IsActive);

public sealed record InventoryListResultPacket(
    Guid RequestId,
    InventoryListPacketStatus Status,
    IReadOnlyList<InventoryListPacketItem> Items)
{
    // Size/Opcode + Guid 문자열 + Status/PartIndex/PartCount/TotalItemCount/ItemCount.
    private const int EnvelopeSize = PacketReader.HeaderSize + 38 + 5 * sizeof(int);
    private const int ItemSize = 3 * sizeof(int) + sizeof(byte);
    public const int MaxItemsPerFrame = (PacketReader.MaxPacketSize - EnvelopeSize) / ItemSize;

    public IEnumerable<byte[]> SerializeFrames(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RequestId == Guid.Empty || !Enum.IsDefined(Status))
        {
            throw new InvalidOperationException("Invalid inventory list result request id or status.");
        }

        ArgumentNullException.ThrowIfNull(Items);
        if (Items.Count > InventoryListQueryService.MaxReturnedItems ||
            (Status != InventoryListPacketStatus.Success && Items.Count != 0))
        {
            throw new InvalidOperationException("Invalid inventory list result item count.");
        }

        // 전체를 검증한 뒤 직렬화해 잘못된 마지막 항목 때문에 일부만 전송되는 것을 막는다.
        var items = Items.ToArray();
        var previousId = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.ItemTemplateId <= previousId ||
                item.Quantity is < 1 or > ItemTemplate.InventoryMaxQuantity ||
                !Enum.IsDefined(item.Type))
            {
                throw new InvalidOperationException("Invalid inventory list item or ordering.");
            }
            previousId = item.ItemTemplateId;
        }

        // 빈 목록/실패도 한 프레임을 반환해 무응답과 구분한다.
        var partCount = Math.Max(1, (items.Length + MaxItemsPerFrame - 1) / MaxItemsPerFrame);
        for (var partIndex = 0; partIndex < partCount; partIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = partIndex * MaxItemsPerFrame;
            var count = Math.Min(MaxItemsPerFrame, items.Length - offset);
            var writer = new PacketWriter();
            writer.WriteUShort(checked((ushort)(EnvelopeSize + count * ItemSize)));
            writer.WriteUShort((ushort)Opcode.InventoryListResult);
            writer.WriteString(RequestId.ToString("D"));
            writer.WriteInt((int)Status);
            writer.WriteInt(partIndex);
            writer.WriteInt(partCount);
            writer.WriteInt(items.Length);
            writer.WriteInt(count);
            for (var index = offset; index < offset + count; index++)
            {
                var item = items[index];
                writer.WriteInt(item.ItemTemplateId);
                writer.WriteInt(item.Quantity);
                writer.WriteInt((int)item.Type);
                writer.WriteBool(item.IsActive);
            }
            yield return writer.ToArray();
        }
    }
}
