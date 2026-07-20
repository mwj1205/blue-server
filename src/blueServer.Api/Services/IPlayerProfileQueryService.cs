using blueServer.Api.DTOs;

namespace blueServer.Api.Services;

public interface IPlayerProfileQueryService
{
    Task<PlayerProfileResponse?> GetAsync(
        long playerId,
        CancellationToken cancellationToken);
}
