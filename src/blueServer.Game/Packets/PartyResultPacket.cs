namespace blueServer.Game.Packets;

public sealed class PartyResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int PartyNo { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<PartySlotPacketItem> Slots { get; init; } = [];

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.PartyResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteInt(PartyNo);
        bodyWriter.WriteString(Name);
        bodyWriter.WriteInt(Slots.Count);

        foreach (var slot in Slots)
        {
            bodyWriter.WriteInt(slot.SlotIndex);
            bodyWriter.WriteLong(slot.OwnedCharacterId);
            bodyWriter.WriteInt(slot.CharacterTemplateId);
            bodyWriter.WriteString(slot.CharacterName);
            bodyWriter.WriteInt(slot.Rarity);
            bodyWriter.WriteString(slot.Role);
            bodyWriter.WriteInt(slot.Level);
            bodyWriter.WriteInt(slot.Star);
            bodyWriter.WriteLong(slot.Exp);
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}

public sealed record PartySlotPacketItem(
    int SlotIndex,
    long OwnedCharacterId,
    int CharacterTemplateId,
    string CharacterName,
    int Rarity,
    string Role,
    int Level,
    int Star,
    long Exp);
