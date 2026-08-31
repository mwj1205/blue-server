namespace blueServer.Game.Packets;

public sealed class MailReadResultPacket
{
    public bool Success { get; init; }
    public MailReadPacketStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? ReadAt { get; init; }

    public byte[] Serialize()
    {
        var successfulStatus = Status is
            MailReadPacketStatus.MarkedAsRead or
            MailReadPacketStatus.AlreadyRead;

        if (Success != successfulStatus ||
            Success != ReadAt.HasValue)
        {
            throw new InvalidOperationException(
                "Mail read success, status, and read time must agree.");
        }

        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailReadResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteInt((int)Status);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteBool(ReadAt.HasValue);

        if (ReadAt.HasValue)
        {
            bodyWriter.WriteLong(
                MailPacketTime.ToUnixMilliseconds(ReadAt.Value));
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}

public enum MailReadPacketStatus
{
    MarkedAsRead = 0,
    AlreadyRead = 1,
    NotFound = 2,
    ConcurrencyConflict = 3
}
