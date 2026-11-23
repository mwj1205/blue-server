namespace blueServer.Game.Packets;

public sealed class MailDetailResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public MailDetailPacketItem? Mail { get; init; }

    public byte[] Serialize()
    {
        if (Success != (Mail is not null))
        {
            throw new InvalidOperationException(
                "Successful mail detail packet must contain mail data, and failed packet must not contain it.");
        }

        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailDetailResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);

        if (Mail is not null)
        {
            bodyWriter.WriteLong(Mail.Id);
            bodyWriter.WriteString(Mail.Title);
            bodyWriter.WriteString(Mail.Body);
            bodyWriter.WriteLong(
                MailPacketTime.ToUnixMilliseconds(Mail.SentAt));
            WriteOptionalTime(bodyWriter, Mail.ExpiresAt);
            WriteOptionalTime(bodyWriter, Mail.ReadAt);
            WriteOptionalTime(bodyWriter, Mail.ClaimedAt);
            bodyWriter.WriteBool(Mail.IsExpired);
            bodyWriter.WriteBool(Mail.CanClaim);
            bodyWriter.WriteInt(Mail.Attachments.Count);

            foreach (var attachment in Mail.Attachments)
            {
                bodyWriter.WriteInt(attachment.RewardType);
                bodyWriter.WriteInt(attachment.Amount);
            }
            if (Mail.ItemAttachments is { } items)
            {
                bodyWriter.WriteInt(items.Count);
                foreach (var item in items)
                {
                    bodyWriter.WriteInt(item.ItemTemplateId);
                    bodyWriter.WriteInt(item.Quantity);
                    bodyWriter.WriteInt(item.ClaimedQuantity);
                    bodyWriter.WriteInt(item.RemainingQuantity);
                }
            }
        }

        var body = bodyWriter.ToArray();
        if (body.Length + 2 > PacketReader.MaxPacketSize)
        {
            throw new PacketProtocolException("Mail detail exceeds maximum packet size.");
        }
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }

    private static void WriteOptionalTime(
        PacketWriter writer,
        DateTime? value)
    {
        writer.WriteBool(value.HasValue);

        if (value.HasValue)
        {
            writer.WriteLong(
                MailPacketTime.ToUnixMilliseconds(value.Value));
        }
    }
}

public sealed record MailDetailPacketItem(
    long Id,
    string Title,
    string Body,
    DateTime SentAt,
    DateTime? ExpiresAt,
    DateTime? ReadAt,
    DateTime? ClaimedAt,
    bool IsExpired,
    bool CanClaim,
    IReadOnlyList<MailAttachmentPacketItem> Attachments)
{
    public IReadOnlyList<MailDetailItemAttachmentPacket>? ItemAttachments { get; init; }
}

public sealed record MailDetailItemAttachmentPacket(int ItemTemplateId, int Quantity, int ClaimedQuantity, int RemainingQuantity);

public sealed record MailAttachmentPacketItem(
    int RewardType,
    int Amount);
