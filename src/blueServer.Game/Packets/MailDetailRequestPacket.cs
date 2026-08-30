namespace blueServer.Game.Packets;

public sealed class MailDetailRequestPacket
{
    public long MailId { get; init; }

    public static MailDetailRequestPacket Read(PacketReader reader)
    {
        var mailId = reader.ReadLong();

        if (mailId <= 0)
        {
            throw new PacketProtocolException(
                $"Mail id must be greater than zero: {mailId}.");
        }

        reader.EnsureFullyRead();

        return new MailDetailRequestPacket
        {
            MailId = mailId
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailDetail);
        bodyWriter.WriteLong(MailId);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
