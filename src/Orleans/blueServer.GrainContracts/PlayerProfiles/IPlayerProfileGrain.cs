namespace blueServer.GrainContracts.PlayerProfiles;

public interface IPlayerProfileGrain : IGrainWithIntegerKey
{
    Task<PlayerProfileSnapshot?> GetProfileAsync();
}
