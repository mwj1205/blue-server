using blueServer.Domain.Entities;

namespace blueServer.Game.Packets;

public sealed class CurrencyBoxUseRequestPacket
{
    public int ItemTemplateId { get; init; }
    public int Quantity { get; init; }
    public Guid RequestId { get; init; }

    public static CurrencyBoxUseRequestPacket Read(PacketReader reader)
    {
        if (reader.Opcode != Opcode.CurrencyBoxUse || reader.RemainingBytes != 46)
        {
            throw new PacketProtocolException("Invalid currency box use opcode or payload length.");
        }

        var itemTemplateId = reader.ReadInt();
        var quantity = reader.ReadInt();
        var text = reader.ReadString();
        reader.EnsureFullyRead();

        if (itemTemplateId <= 0 || quantity is < 1 or > ItemTemplate.InventoryMaxQuantity ||
            !Guid.TryParseExact(text, "D", out var requestId) || requestId == Guid.Empty)
        {
            throw new PacketProtocolException("Invalid currency box use item, quantity, or request id.");
        }

        return new CurrencyBoxUseRequestPacket
        {
            ItemTemplateId = itemTemplateId,
            Quantity = quantity,
            RequestId = requestId
        };
    }

    public byte[] Serialize()
    {
        if (ItemTemplateId <= 0 || Quantity is < 1 or > ItemTemplate.InventoryMaxQuantity || RequestId == Guid.Empty)
        {
            throw new InvalidOperationException("Invalid currency box use request.");
        }

        var writer = new PacketWriter();
        writer.WriteUShort(50);
        writer.WriteUShort((ushort)Opcode.CurrencyBoxUse);
        writer.WriteInt(ItemTemplateId);
        writer.WriteInt(Quantity);
        writer.WriteString(RequestId.ToString("D"));
        return writer.ToArray();
    }
}
