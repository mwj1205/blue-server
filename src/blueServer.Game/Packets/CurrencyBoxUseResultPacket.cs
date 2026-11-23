using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;

namespace blueServer.Game.Packets;

public enum CurrencyBoxUsePacketStatus
{
    Used = 0,
    PlayerNotFound = 1,
    BoxNotFound = 2,
    InsufficientQuantity = 3,
    ItemTemplateNotFound = 4,
    ItemTemplateInactive = 5,
    IdempotencyConflict = 6,
    ConcurrencyConflict = 7
}

public sealed record CurrencyBoxUsePacketSnapshot(
    int ItemTemplateId,
    int UsedQuantity,
    int QuantityAfter,
    CurrencyType CurrencyType,
    int GrantedAmount,
    int CurrentGold,
    int CurrentGem,
    DateTime ProcessedAt);

public sealed class CurrencyBoxUseResultPacket
{
    public Guid RequestId { get; init; }
    public CurrencyBoxUsePacketStatus Status { get; init; }
    public CurrencyBoxUsePacketSnapshot? Snapshot { get; init; }

    public byte[] Serialize()
    {
        if (RequestId == Guid.Empty || !Enum.IsDefined(Status) ||
            (Status == CurrencyBoxUsePacketStatus.Used) != (Snapshot is not null))
        {
            throw new InvalidOperationException("Currency box use status and snapshot must agree.");
        }

        if (Snapshot is { } value &&
            (value.ItemTemplateId <= 0 || value.UsedQuantity is < 1 or > ItemTemplate.InventoryMaxQuantity ||
             value.QuantityAfter < 0 || (long)value.UsedQuantity + value.QuantityAfter > ItemTemplate.InventoryMaxQuantity ||
             !Enum.IsDefined(value.CurrencyType) || value.GrantedAmount <= 0 ||
             value.CurrentGold < 0 || value.CurrentGem < 0 ||
             (value.CurrencyType == CurrencyType.Gold ? value.CurrentGold : value.CurrentGem) < value.GrantedAmount ||
             value.ProcessedAt.Kind != DateTimeKind.Utc))
        {
            throw new InvalidOperationException("Invalid currency box use snapshot.");
        }

        var writer = new PacketWriter();
        // 상태별 고정 길이이므로 가변 메시지나 분할 전송은 필요 없다.
        writer.WriteUShort((ushort)(Snapshot is null ? 47 : 83));
        writer.WriteUShort((ushort)Opcode.CurrencyBoxUseResult);
        writer.WriteInt((int)Status);
        writer.WriteString(RequestId.ToString("D"));
        writer.WriteBool(Snapshot is not null);
        if (Snapshot is { } snapshot)
        {
            writer.WriteInt(snapshot.ItemTemplateId);
            writer.WriteInt(snapshot.UsedQuantity);
            writer.WriteInt(snapshot.QuantityAfter);
            writer.WriteInt((int)snapshot.CurrencyType);
            writer.WriteInt(snapshot.GrantedAmount);
            writer.WriteInt(snapshot.CurrentGold);
            writer.WriteInt(snapshot.CurrentGem);
            writer.WriteLong(new DateTimeOffset(snapshot.ProcessedAt).ToUnixTimeMilliseconds());
        }

        return writer.ToArray();
    }
}
