using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

internal static class MailPacketMapper
{
    public static MailListResultPacket ToPacket(MailListResult result)
    {
        return new MailListResultPacket
        {
            Success = result.IsSuccess,
            Message = result.IsSuccess
                ? "Mail list loaded"
                : "Player not found",
            Items = result.Items
                .Select(item => new MailListPacketItem(
                    item.Id,
                    item.Title,
                    item.SentAt,
                    item.ExpiresAt,
                    item.IsRead,
                    item.IsClaimed,
                    item.IsExpired,
                    item.CanClaim,
                    item.AttachmentCount))
                .ToArray(),
            NextCursor = result.NextCursor
        };
    }

    public static MailDetailResultPacket ToPacket(MailDetailResult result)
    {
        if (result.Mail is null)
        {
            return new MailDetailResultPacket
            {
                Success = false,
                Message = "Mail not found"
            };
        }

        return new MailDetailResultPacket
        {
            Success = true,
            Message = "Mail detail loaded",
            Mail = new MailDetailPacketItem(
                result.Mail.Id,
                result.Mail.Title,
                result.Mail.Body,
                result.Mail.SentAt,
                result.Mail.ExpiresAt,
                result.Mail.ReadAt,
                result.Mail.ClaimedAt,
                result.Mail.IsExpired,
                result.Mail.CanClaim,
                result.Mail.Attachments
                    .Select(attachment => new MailAttachmentPacketItem(
                        (int)attachment.Type,
                        attachment.Amount))
                    .ToArray())
        };
    }
}
