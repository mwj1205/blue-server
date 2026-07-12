namespace blueServer.Game.Packets;

public sealed class PartyGetRequestPacket
{
    public int PartyNo { get; init; }

    public static PartyGetRequestPacket Read(PacketReader reader)
    {
        return new PartyGetRequestPacket
        {
            PartyNo = reader.ReadInt()
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.PartyGet);
        bodyWriter.WriteInt(PartyNo);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort((ushort)(body.Length + 2));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
