namespace blueServer.Game.Packets;

public sealed class MailReadRequestPacket
{
    public long MailId { get; init; }

    public static MailReadRequestPacket Read(PacketReader reader)
    {
        var mailId = reader.ReadLong();

        if (mailId <= 0)
        {
            throw new PacketProtocolException(
                $"Mail id must be greater than zero: {mailId}.");
        }

        reader.EnsureFullyRead();

        return new MailReadRequestPacket
        {
            MailId = mailId
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailRead);
        bodyWriter.WriteLong(MailId);

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
