namespace blueServer.Game.Packets;

public sealed class StageClearRequestPacket
{
    public int StageTemplateId { get; init; }
    public int PartyNo { get; init; }

    public static StageClearRequestPacket Read(PacketReader reader)
    {
        return new StageClearRequestPacket
        {
            StageTemplateId = reader.ReadInt(),
            PartyNo = reader.ReadInt()
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.StageClear);
        bodyWriter.WriteInt(StageTemplateId);
        bodyWriter.WriteInt(PartyNo);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
