using blueServer.Api.DTOs;
using blueServer.GrainContracts.PlayerProfiles;
using Orleans;

namespace blueServer.Api.Services;

public sealed class OrleansPlayerProfileQueryService :
    IPlayerProfileQueryService
{
    private readonly IGrainFactory _grainFactory;

    public OrleansPlayerProfileQueryService(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<PlayerProfileResponse?> GetAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        var grain = _grainFactory.GetGrain<IPlayerProfileGrain>(playerId);
        var profile = await grain.GetProfileAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        return new PlayerProfileResponse
        {
            Id = profile.Id,
            Nickname = profile.Nickname,
            Gold = profile.Gold,
            Gem = profile.Gem,
            OwnedCharacterCount = profile.OwnedCharacterCount,
            PartyCount = profile.PartyCount,
            ClearedStageCount = profile.ClearedStageCount,
            TotalStageClearCount = profile.TotalStageClearCount
        };
    }
}
