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
    IReadOnlyList<MailAttachmentResponse> Attachments)
{
    public IReadOnlyList<MailItemAttachmentResponse> ItemAttachments { get; init; } =
        Array.Empty<MailItemAttachmentResponse>();
}

public sealed record MailAttachmentResponse(
    RewardType Type,
    int Amount);

public sealed record MailItemAttachmentResponse(
    int ItemTemplateId,
    int Quantity,
    int ClaimedQuantity,
    int RemainingQuantity);
