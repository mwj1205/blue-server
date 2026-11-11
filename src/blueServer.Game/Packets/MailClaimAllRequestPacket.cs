namespace blueServer.Game.Packets;

public sealed class MailClaimAllRequestPacket
{
    public Guid? RequestId { get; init; }

    public static MailClaimAllRequestPacket Read(PacketReader reader)
    {
        Guid? requestId = null;
        if (reader.RemainingBytes > 0)
        {
            if (reader.RemainingBytes != 38 ||
                !Guid.TryParseExact(reader.ReadString(), "D", out var parsed) ||
                parsed == Guid.Empty)
            {
                throw new PacketProtocolException(
                    "Mail claim-all request id must be a non-empty D-format GUID.");
            }

            requestId = parsed;
        }

        reader.EnsureFullyRead();
        return new MailClaimAllRequestPacket { RequestId = requestId };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailClaimAll);

        if (RequestId.HasValue)
        {
            if (RequestId.Value == Guid.Empty)
            {
                throw new InvalidOperationException("Mail claim-all request id must not be empty.");
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
