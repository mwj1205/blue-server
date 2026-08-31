namespace blueServer.Game.Packets;

public sealed class MailClaimAllRequestPacket
{
    public static MailClaimAllRequestPacket Read(PacketReader reader)
    {
        reader.EnsureFullyRead();
        return new MailClaimAllRequestPacket();
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailClaimAll);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
