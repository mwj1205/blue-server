using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Packets;

public sealed class MailListRequestPacket
{
    public int PageSize { get; init; } =
        MailListQueryService.DefaultPageSize;
    public MailListCursor? Cursor { get; init; }

    public static MailListRequestPacket Read(PacketReader reader)
    {
        var pageSize = reader.ReadInt();

        if (pageSize is < 1 or > MailListQueryService.MaxPageSize)
        {
            throw new PacketProtocolException(
                $"Mail page size must be between 1 and {MailListQueryService.MaxPageSize}: {pageSize}.");
        }

        MailListCursor? cursor = null;

        // Keyset Pagination 재개 지점의 선택적 복원
        if (reader.ReadBool())
        {
            var sentAt = MailPacketTime.FromUnixMilliseconds(
                reader.ReadLong());
            var mailId = reader.ReadLong();

            if (mailId <= 0)
            {
                throw new PacketProtocolException(
                    $"Mail cursor id must be greater than zero: {mailId}.");
            }

            cursor = new MailListCursor(sentAt, mailId);
        }

        reader.EnsureFullyRead();

        return new MailListRequestPacket
        {
            PageSize = pageSize,
            Cursor = cursor
        };
    }

    public byte[] Serialize()
    {
        var bodyWriter = new PacketWriter();

        bodyWriter.WriteUShort((ushort)Opcode.MailList);
        bodyWriter.WriteInt(PageSize);
        bodyWriter.WriteBool(Cursor is not null);

        // Client와 서버의 플랫폼 차이를 제거하는 UTC Unix time 전송
        if (Cursor is not null)
        {
            bodyWriter.WriteLong(
                MailPacketTime.ToUnixMilliseconds(Cursor.SentAt));
            bodyWriter.WriteLong(Cursor.Id);
        }

        var body = bodyWriter.ToArray();
        var finalWriter = new PacketWriter();

        finalWriter.WriteUShort(checked((ushort)(body.Length + 2)));
        finalWriter.WriteBytes(body);

        return finalWriter.ToArray();
    }
}
