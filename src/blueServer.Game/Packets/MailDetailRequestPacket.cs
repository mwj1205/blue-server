namespace blueServer.Game.Packets;

public sealed class MailDetailRequestPacket
{
    public long MailId { get; init; }
    public bool IncludeItemAttachments { get; init; }

    public static MailDetailRequestPacket Read(PacketReader reader)
    {
        var mailId = reader.ReadLong();

        if (mailId <= 0)
        {
            throw new PacketProtocolException(
                $"Mail id must be greater than zero: {mailId}.");
        }

        var includeItems = reader.RemainingBytes > 0 && reader.ReadBool();
        reader.EnsureFullyRead();

        return new MailDetailRequestPacket
        {
            MailId = mailId,
            IncludeItemAttachments = includeItems
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailDetail);
        bodyWriter.WriteLong(MailId);
        if (IncludeItemAttachments)
        {
            bodyWriter.WriteBool(true);
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
