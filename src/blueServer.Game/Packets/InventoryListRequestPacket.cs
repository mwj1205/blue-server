namespace blueServer.Game.Packets;

public sealed record InventoryListRequestPacket(Guid RequestId)
{
    public static InventoryListRequestPacket Read(PacketReader reader)
    {
        if (reader.Opcode != Opcode.InventoryList || reader.RemainingBytes != 38)
        {
            throw new PacketProtocolException("Invalid inventory list opcode or payload length.");
        }

        var text = reader.ReadString();
        reader.EnsureFullyRead();
        if (!Guid.TryParseExact(text, "D", out var requestId) || requestId == Guid.Empty)
        {
            throw new PacketProtocolException("Invalid inventory list request id.");
        }

        return new InventoryListRequestPacket(requestId);
    }

    public byte[] Serialize()
    {
        if (RequestId == Guid.Empty)
        {
            throw new InvalidOperationException("Inventory list request id must not be empty.");
        }

        var writer = new PacketWriter();
        writer.WriteUShort(42);
        writer.WriteUShort((ushort)Opcode.InventoryList);
        writer.WriteString(RequestId.ToString("D"));
        return writer.ToArray();
    }
}
