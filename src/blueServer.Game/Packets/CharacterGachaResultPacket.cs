namespace blueServer.Game.Packets;

public sealed class CharacterGachaResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public long OwnedCharacterId { get; init; }
    public int CharacterTemplateId { get; init; }
    public string CharacterName { get; init; } = string.Empty;
    public int Rarity { get; init; }
    public int RemainingGem { get; init; }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.CharacterGachaResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteLong(OwnedCharacterId);
        bodyWriter.WriteInt(CharacterTemplateId);
        bodyWriter.WriteString(CharacterName);
        bodyWriter.WriteInt(Rarity);
        bodyWriter.WriteInt(RemainingGem);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
