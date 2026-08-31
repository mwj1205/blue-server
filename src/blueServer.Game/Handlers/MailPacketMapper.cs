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

    public static MailClaimResultPacket ToPacket(MailClaimResult result)
    {
        var status = result.Status switch
        {
            MailClaimStatus.Claimed => MailClaimPacketStatus.Claimed,
            MailClaimStatus.AlreadyClaimed =>
                MailClaimPacketStatus.AlreadyClaimed,
            MailClaimStatus.NotFound or MailClaimStatus.PlayerNotFound =>
                MailClaimPacketStatus.NotFound,
            MailClaimStatus.Expired => MailClaimPacketStatus.Expired,
            MailClaimStatus.NoRewards => MailClaimPacketStatus.NoRewards,
            MailClaimStatus.ConcurrencyConflict =>
                MailClaimPacketStatus.ConcurrencyConflict,
            MailClaimStatus.IdempotencyConflict =>
                MailClaimPacketStatus.IdempotencyConflict,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status),
                result.Status,
                "Unexpected Mail claim status.")
        };

        return new MailClaimResultPacket
        {
            Success = result.IsSuccess,
            Status = status,
            Message = GetClaimMessage(status),
            ClaimedAt = result.ClaimedAt,
            CurrentGold = result.CurrentGold,
            CurrentGem = result.CurrentGem
        };
    }

    public static MailClaimAllResultPacket ToPacket(
        MailClaimAllResult result)
    {
        var status = result.Status switch
        {
            MailClaimAllStatus.Claimed =>
                MailClaimAllPacketStatus.Claimed,
            MailClaimAllStatus.NothingToClaim =>
                MailClaimAllPacketStatus.NothingToClaim,
            MailClaimAllStatus.PlayerNotFound =>
                MailClaimAllPacketStatus.PlayerNotFound,
            MailClaimAllStatus.ConcurrencyConflict =>
                MailClaimAllPacketStatus.ConcurrencyConflict,
            MailClaimAllStatus.IdempotencyConflict =>
                MailClaimAllPacketStatus.IdempotencyConflict,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status),
                result.Status,
                "Unexpected Mail claim-all status.")
        };

        return new MailClaimAllResultPacket
        {
            Success = result.IsSuccess,
            Status = status,
            Message = GetClaimAllMessage(status),
            ClaimedMailCount = result.ClaimedMailCount,
            GrantedGold = result.GrantedGold,
            GrantedGem = result.GrantedGem,
            CurrentGold = result.CurrentGold,
            CurrentGem = result.CurrentGem,
            HasMore = result.HasMore
        };
    }

    public static MailReadResultPacket ToPacket(MailReadResult result)
    {
        var status = result.Status switch
        {
            MailReadStatus.MarkedAsRead =>
                MailReadPacketStatus.MarkedAsRead,
            MailReadStatus.AlreadyRead =>
                MailReadPacketStatus.AlreadyRead,
            MailReadStatus.NotFound => MailReadPacketStatus.NotFound,
            MailReadStatus.ConcurrencyConflict =>
                MailReadPacketStatus.ConcurrencyConflict,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status),
                result.Status,
                "Unexpected Mail read status.")
        };

        return new MailReadResultPacket
        {
            Success = result.IsSuccess,
            Status = status,
            Message = GetReadMessage(status),
            ReadAt = result.ReadAt
        };
    }

    private static string GetClaimMessage(MailClaimPacketStatus status)
    {
        return status switch
        {
            MailClaimPacketStatus.Claimed => "Mail rewards claimed",
            MailClaimPacketStatus.AlreadyClaimed =>
                "Mail rewards already claimed",
            MailClaimPacketStatus.NotFound => "Mail not found",
            MailClaimPacketStatus.Expired => "Mail has expired",
            MailClaimPacketStatus.NoRewards =>
                "Mail has no rewards to claim",
            MailClaimPacketStatus.ConcurrencyConflict =>
                "Mail state changed. Reload the Mail and try again",
            MailClaimPacketStatus.IdempotencyConflict =>
                "Mail reward state conflicts with the completed request",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unexpected Mail claim packet status.")
        };
    }

    private static string GetClaimAllMessage(
        MailClaimAllPacketStatus status)
    {
        return status switch
        {
            MailClaimAllPacketStatus.Claimed =>
                "Mail rewards claimed",
            MailClaimAllPacketStatus.NothingToClaim =>
                "No Mail rewards to claim",
            MailClaimAllPacketStatus.PlayerNotFound =>
                "Player not found",
            MailClaimAllPacketStatus.ConcurrencyConflict =>
                "Mail state changed. Reload the Mail list and try again",
            MailClaimAllPacketStatus.IdempotencyConflict =>
                "Mail reward state conflicts with a completed request",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unexpected Mail claim-all packet status.")
        };
    }

    private static string GetReadMessage(MailReadPacketStatus status)
    {
        return status switch
        {
            MailReadPacketStatus.MarkedAsRead => "Mail marked as read",
            MailReadPacketStatus.AlreadyRead => "Mail already read",
            MailReadPacketStatus.NotFound => "Mail not found",
            MailReadPacketStatus.ConcurrencyConflict =>
                "Mail state changed. Reload the Mail and try again",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unexpected Mail read packet status.")
        };
    }
}
