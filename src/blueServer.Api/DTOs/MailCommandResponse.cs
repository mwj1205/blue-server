namespace blueServer.Api.DTOs;

public sealed record MailReadResponse(
    DateTime ReadAt,
    bool WasAlreadyRead);

public sealed record MailClaimResponse(
    DateTime ClaimedAt,
    int CurrentGold,
    int CurrentGem,
    bool WasAlreadyClaimed);

public sealed record MailClaimAllResponse(
    int ClaimedMailCount,
    int GrantedGold,
    int GrantedGem,
    int CurrentGold,
    int CurrentGem,
    bool HasMore);
