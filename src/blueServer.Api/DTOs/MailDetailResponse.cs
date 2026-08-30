using blueServer.Domain.Rewards;

namespace blueServer.Api.DTOs;

public sealed record MailDetailResponse(
    long Id,
    string Title,
    string Body,
    DateTime SentAt,
    DateTime? ExpiresAt,
    DateTime? ReadAt,
    DateTime? ClaimedAt,
    bool IsRead,
    bool IsClaimed,
    bool IsExpired,
    bool CanClaim,
    IReadOnlyList<MailAttachmentResponse> Attachments);

public sealed record MailAttachmentResponse(
    RewardType Type,
    int Amount);
