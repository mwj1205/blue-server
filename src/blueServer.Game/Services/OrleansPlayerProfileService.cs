using blueServer.GrainContracts.PlayerProfiles;
using Orleans;

namespace blueServer.Game.Services;

public sealed class OrleansPlayerProfileService : IPlayerProfileService
{
    private readonly IGrainFactory _grainFactory;

    public OrleansPlayerProfileService(IGrainFactory grainFactory)
    {
        _grainFactory = grainFactory;
    }

    public async Task<PlayerProfileResult> GetAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        var grain = _grainFactory.GetGrain<IPlayerProfileGrain>(playerId);
        var profile = await grain.GetProfileAsync(cancellationToken);

        if (profile is null)
        {
            return PlayerProfileResult.Fail("Player not found");
        }

        return new PlayerProfileResult(
            true,
            "Player profile loaded",
            profile.Id,
            profile.Nickname,
            profile.Gold,
            profile.Gem,
            profile.OwnedCharacterCount,
            profile.PartyCount,
            profile.ClearedStageCount,
            profile.TotalStageClearCount);
    }
}
