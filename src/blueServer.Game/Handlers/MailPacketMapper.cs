using blueServer.Game.Packets;
using blueServer.Infrastructure.Mails;

namespace blueServer.Game.Handlers;

internal static class MailPacketMapper
{
    public static MailClaimResultPacket ToPacket(
        MailClaimRequestResult result,
        Guid requestId)
    {
        var status = result.Status switch
        {
            MailClaimRequestStatus.Claimed => MailClaimPacketStatus.Claimed,
            MailClaimRequestStatus.AlreadyClaimed => MailClaimPacketStatus.AlreadyClaimed,
            MailClaimRequestStatus.PartiallyClaimed => MailClaimPacketStatus.PartiallyClaimed,
            MailClaimRequestStatus.NoCapacity => MailClaimPacketStatus.NoCapacity,
            MailClaimRequestStatus.NotFound or
                MailClaimRequestStatus.PlayerNotFound => MailClaimPacketStatus.NotFound,
            MailClaimRequestStatus.Expired => MailClaimPacketStatus.Expired,
            MailClaimRequestStatus.NoRewards => MailClaimPacketStatus.NoRewards,
            MailClaimRequestStatus.ConcurrencyConflict => MailClaimPacketStatus.ConcurrencyConflict,
            MailClaimRequestStatus.IdempotencyConflict => MailClaimPacketStatus.IdempotencyConflict,
            MailClaimRequestStatus.ItemTemplateNotFound => MailClaimPacketStatus.ItemTemplateNotFound,
            MailClaimRequestStatus.ItemTemplateInactive => MailClaimPacketStatus.ItemTemplateInactive,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status),
                result.Status,
                "Unexpected Mail claim status.")
        };

        return new MailClaimResultPacket
        {
            Success = status is
                MailClaimPacketStatus.Claimed or
                MailClaimPacketStatus.AlreadyClaimed or
                MailClaimPacketStatus.PartiallyClaimed or
                MailClaimPacketStatus.NoCapacity,
            Status = status,
            Message = status.ToString(),
            RequestId = requestId,
            ProcessedAt = result.ProcessedAt,
            ClaimedAt = result.MailClaimedAt,
            GrantedGold = result.GrantedGold,
            GrantedGem = result.GrantedGem,
            CurrentGold = result.CurrentGold,
            CurrentGem = result.CurrentGem,
            Items = result.Items
                .Select(item => new MailClaimPacketItem(
                    item.ItemTemplateId,
                    item.GrantedQuantity,
                    item.RemainingQuantity,
                    item.QuantityAfter))
                .ToArray()
        };
    }

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
            MailClaimStatus.NoRewards or MailClaimStatus.RequestIdRequired => MailClaimPacketStatus.NoRewards,
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
            Message = result.Status == MailClaimStatus.RequestIdRequired
                ? "Item rewards require a RequestId-capable claim request"
                : GetClaimMessage(status),
            ClaimedAt = result.ClaimedAt,
            CurrentGold = result.CurrentGold,
            CurrentGem = result.CurrentGem
        };
    }

    public static MailClaimAllRequestResultPacket ToPacket(
        MailClaimAllRequestResult result,
        Guid requestId)
    {
        var status = result.Status switch
        {
            MailClaimAllRequestStatus.Processed => MailClaimAllPacketStatus.Processed,
            MailClaimAllRequestStatus.NothingToClaim => MailClaimAllPacketStatus.NothingToClaim,
            MailClaimAllRequestStatus.PlayerNotFound => MailClaimAllPacketStatus.PlayerNotFound,
            MailClaimAllRequestStatus.ConcurrencyConflict => MailClaimAllPacketStatus.ConcurrencyConflict,
            MailClaimAllRequestStatus.IdempotencyConflict => MailClaimAllPacketStatus.IdempotencyConflict,
            MailClaimAllRequestStatus.ItemTemplateNotFound => MailClaimAllPacketStatus.ItemTemplateNotFound,
            MailClaimAllRequestStatus.ItemTemplateInactive => MailClaimAllPacketStatus.ItemTemplateInactive,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result.Status),
                result.Status,
                "Unexpected Mail claim-all request status.")
        };

        return new MailClaimAllRequestResultPacket(
            requestId,
            status,
            result.ProcessedAt,
            result.ClaimedMailCount,
            result.GrantedGold,
            result.GrantedGem,
            result.CurrentGold,
            result.CurrentGem,
            result.HasMore,
            result.Entries.Select(ToPacket).ToArray());
    }

    private static MailClaimAllPacketEntry ToPacket(MailClaimAllEntryResult entry)
    {
        var claim = entry.Claim;
        var status = claim.Status switch
        {
            MailClaimRequestStatus.Claimed => MailClaimPacketStatus.Claimed,
            MailClaimRequestStatus.PartiallyClaimed => MailClaimPacketStatus.PartiallyClaimed,
            MailClaimRequestStatus.NoCapacity => MailClaimPacketStatus.NoCapacity,
            _ => throw new ArgumentOutOfRangeException(
                nameof(entry),
                claim.Status,
                "Unexpected Mail claim-all entry status.")
        };

        return new MailClaimAllPacketEntry(
            entry.MailId,
            status,
            claim.MailClaimedAt,
            claim.GrantedGold,
            claim.GrantedGem,
            claim.Items.Select(item => new MailClaimPacketItem(
                item.ItemTemplateId,
                item.GrantedQuantity,
                item.RemainingQuantity,
                item.QuantityAfter)).ToArray());
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
