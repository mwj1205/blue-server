namespace blueServer.Api.DTOs;

public sealed record MailReadResponse(
    DateTime ReadAt,
    bool WasAlreadyRead);

public sealed record MailClaimResponse(
    DateTime? ClaimedAt,
    int CurrentGold,
    int CurrentGem,
    bool WasAlreadyClaimed,
    Guid RequestId,
    string Status,
    DateTime? ProcessedAt,
    int GrantedGold,
    int GrantedGem,
    IReadOnlyList<MailClaimItemResponse> Items);

public sealed record MailClaimBody(Guid RequestId);

public sealed record MailClaimItemResponse(
    int ItemTemplateId,
    int GrantedQuantity,
    int RemainingQuantity,
    int QuantityAfter);

public sealed record MailClaimAllResponse(
    int ClaimedMailCount,
    int GrantedGold,
    int GrantedGem,
    int CurrentGold,
    int CurrentGem,
    bool HasMore);
