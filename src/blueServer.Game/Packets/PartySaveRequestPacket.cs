using blueServer.Domain.Entities;

namespace blueServer.Game.Packets;

public sealed class PartySaveRequestPacket
{
    public int PartyNo { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<PartySaveSlotPacketItem> Slots { get; init; } = [];

    public static PartySaveRequestPacket Read(PacketReader reader)
    {
        var partyNo = reader.ReadInt();
        var name = reader.ReadString();
        var slotCount = reader.ReadInt();

        if (slotCount is < 0 or > PartySlot.MaxSlotIndex)
        {
            throw new PacketProtocolException(
                $"Invalid party slot count: {slotCount}.");
        }

        var slots = new PartySaveSlotPacketItem[slotCount];

        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = new PartySaveSlotPacketItem(
                reader.ReadInt(),
                reader.ReadLong());
        }

        return new PartySaveRequestPacket
        {
            PartyNo = partyNo,
            Name = name,
            Slots = slots
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.PartySave);
        bodyWriter.WriteInt(PartyNo);
        bodyWriter.WriteString(Name);
        bodyWriter.WriteInt(Slots.Count);

        foreach (var slot in Slots)
        {
            bodyWriter.WriteInt(slot.SlotIndex);
            bodyWriter.WriteLong(slot.OwnedCharacterId);
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}

public sealed record PartySaveSlotPacketItem(
    int SlotIndex,
    long OwnedCharacterId);
