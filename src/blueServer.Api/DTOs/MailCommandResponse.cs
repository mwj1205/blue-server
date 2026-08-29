namespace blueServer.Api.DTOs;

public sealed record MailReadResponse(
    DateTime ReadAt,
    bool WasAlreadyRead);

public sealed record MailClaimResponse(
    DateTime ClaimedAt,
    int CurrentGold,
    int CurrentGem,
    bool WasAlreadyClaimed);
