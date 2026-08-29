using System.Security.Claims;

namespace blueServer.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetPlayerId(
        this ClaimsPrincipal principal,
        out long playerId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var playerIdClaim = principal.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return long.TryParse(
            playerIdClaim,
            out playerId) &&
            playerId > 0;
    }
}
