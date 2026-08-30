using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Packets;

public sealed class MailListResultPacket
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<MailListPacketItem> Items { get; init; } = [];
    public MailListCursor? NextCursor { get; init; }

    public byte[] Serialize()
    {
        if (Items.Count > MailListQueryService.MaxPageSize)
        {
            throw new InvalidOperationException(
                $"Mail list packet cannot contain more than {MailListQueryService.MaxPageSize} items.");
        }

        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailListResult);
        bodyWriter.WriteBool(Success);
        bodyWriter.WriteString(Message);
        bodyWriter.WriteInt(Items.Count);

        foreach (var item in Items)
        {
            bodyWriter.WriteLong(item.Id);
            bodyWriter.WriteString(item.Title);
            bodyWriter.WriteLong(
                MailPacketTime.ToUnixMilliseconds(item.SentAt));
            WriteOptionalTime(bodyWriter, item.ExpiresAt);
            bodyWriter.WriteBool(item.IsRead);
            bodyWriter.WriteBool(item.IsClaimed);
            bodyWriter.WriteBool(item.IsExpired);
            bodyWriter.WriteBool(item.CanClaim);
            bodyWriter.WriteInt(item.AttachmentCount);
        }

        bodyWriter.WriteBool(NextCursor is not null);

        if (NextCursor is not null)
        {
            bodyWriter.WriteLong(
                MailPacketTime.ToUnixMilliseconds(NextCursor.SentAt));
            bodyWriter.WriteLong(NextCursor.Id);
        }

        var body = bodyWriter.ToArray();
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

public sealed record MailListPacketItem(
    long Id,
    string Title,
    DateTime SentAt,
    DateTime? ExpiresAt,
    bool IsRead,
    bool IsClaimed,
    bool IsExpired,
    bool CanClaim,
    int AttachmentCount);
