namespace blueServer.Api.DTOs;

public sealed record MailListResponse(
    IReadOnlyList<MailListItemResponse> Items,
    MailListCursorResponse? NextCursor);

public sealed record MailListItemResponse(
    long Id,
    string Title,
    DateTime SentAt,
    DateTime? ExpiresAt,
    bool IsRead,
    bool IsClaimed,
    bool IsExpired,
    bool CanClaim,
    int AttachmentCount);

public sealed record MailListCursorResponse(
    DateTime SentAt,
    long Id);
