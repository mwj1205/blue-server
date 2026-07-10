namespace blueServer.Game.Packets;

public sealed class StageClearResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int StageTemplateId { get; init; }
    public string StageName { get; init; } = string.Empty;
    public int PartyNo { get; init; }
    public int RewardGold { get; init; }
    public int RewardGem { get; init; }
    public int CurrentGold { get; init; }
    public int CurrentGem { get; init; }
    public int ClearCount { get; init; }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.StageClearResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteInt(StageTemplateId);
        bodyWriter.WriteString(StageName);
        bodyWriter.WriteInt(PartyNo);
        bodyWriter.WriteInt(RewardGold);
        bodyWriter.WriteInt(RewardGem);
        bodyWriter.WriteInt(CurrentGold);
        bodyWriter.WriteInt(CurrentGem);
        bodyWriter.WriteInt(ClearCount);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
