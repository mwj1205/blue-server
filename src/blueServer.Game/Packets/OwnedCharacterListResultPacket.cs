namespace blueServer.Game.Packets;

public sealed class OwnedCharacterListResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<OwnedCharacterListPacketItem> Characters { get; init; } = [];

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.OwnedCharacterListResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteInt(Characters.Count);

        foreach (var character in Characters)
        {
            bodyWriter.WriteLong(character.Id);
            bodyWriter.WriteInt(character.CharacterTemplateId);
            bodyWriter.WriteString(character.CharacterName);
            bodyWriter.WriteInt(character.Rarity);
            bodyWriter.WriteString(character.Role);
            bodyWriter.WriteInt(character.Level);
            bodyWriter.WriteInt(character.Star);
            bodyWriter.WriteLong(character.Exp);
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}

public sealed record OwnedCharacterListPacketItem(
    long Id,
    int CharacterTemplateId,
    string CharacterName,
    int Rarity,
    string Role,
    int Level,
    int Star,
    long Exp);
