namespace blueServer.Game.Packets;

public sealed class PlayerProfileResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public long PlayerId { get; init; }
    public string Nickname { get; init; } = string.Empty;
    public int Gold { get; init; }
    public int Gem { get; init; }
    public int OwnedCharacterCount { get; init; }
    public int PartyCount { get; init; }
    public int ClearedStageCount { get; init; }
    public int TotalStageClearCount { get; init; }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.PlayerProfileResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteLong(PlayerId);
        bodyWriter.WriteString(Nickname);
        bodyWriter.WriteInt(Gold);
        bodyWriter.WriteInt(Gem);
        bodyWriter.WriteInt(OwnedCharacterCount);
        bodyWriter.WriteInt(PartyCount);
        bodyWriter.WriteInt(ClearedStageCount);
        bodyWriter.WriteInt(TotalStageClearCount);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
