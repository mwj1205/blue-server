namespace blueServer.Game.Packets;

public sealed class MailClaimRequestPacket
{
    public long MailId { get; init; }
    public Guid? RequestId { get; init; }

    public static MailClaimRequestPacket Read(PacketReader reader)
    {
        var mailId = reader.ReadLong();

        if (mailId <= 0)
        {
            throw new PacketProtocolException(
                $"Mail id must be greater than zero: {mailId}.");
        }

        Guid? requestId = null;

        if (reader.RemainingBytes > 0)
        {
            // 기존 MailId 전용 요청과 구분하며 GUID는 D 형식 문자열로 전송한다.
            if (reader.RemainingBytes != 38 ||
                !Guid.TryParseExact(reader.ReadString(), "D", out var parsed) ||
                parsed == Guid.Empty)
            {
                throw new PacketProtocolException(
                    "Mail claim request id must be a non-empty D-format GUID.");
            }

            requestId = parsed;
        }

        reader.EnsureFullyRead();

        return new MailClaimRequestPacket
        {
            MailId = mailId,
            RequestId = requestId
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailClaim);
        bodyWriter.WriteLong(MailId);

        if (RequestId.HasValue)
        {
            if (RequestId.Value == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Mail claim request id must not be empty.");
            }

            bodyWriter.WriteString(RequestId.Value.ToString("D"));
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
