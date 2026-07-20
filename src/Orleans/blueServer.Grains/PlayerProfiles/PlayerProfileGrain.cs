using blueServer.GrainContracts.PlayerProfiles;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace blueServer.Grains.PlayerProfiles;

public sealed class PlayerProfileGrain :
    Grain,
    IPlayerProfileGrain
{
    private readonly IDbContextFactory<GameDbContext> _dbContextFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<PlayerProfileGrain> _logger;

    public PlayerProfileGrain(
        IDbContextFactory<GameDbContext> dbContextFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<PlayerProfileGrain> logger)
    {
        _dbContextFactory = dbContextFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public override Task OnActivateAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            LogEventIds.Orleans.PlayerProfileGrainActivated,
            "PlayerProfile grain activated. PlayerId={PlayerId}, Silo={Silo}",
            this.GetPrimaryKeyLong(),
            this.RuntimeIdentity);

        return base.OnActivateAsync(cancellationToken);
    }

    public async Task<PlayerProfileSnapshot?> GetProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var playerId = this.GetPrimaryKeyLong();

        if (playerId <= 0)
        {
            return null;
        }

        using var operationCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _applicationLifetime.ApplicationStopping);
        var operationCancellationToken =
            operationCancellationSource.Token;

        await using var dbContext = await _dbContextFactory
            .CreateDbContextAsync(operationCancellationToken);

        return await dbContext.Players
            .AsNoTracking()
            .Where(player => player.Id == playerId)
            .Select(player => new PlayerProfileSnapshot
            {
                Id = player.Id,
                Nickname = player.Nickname,
                Gold = player.Gold,
                Gem = player.Gem,
                OwnedCharacterCount = player.OwnedCharacters.Count,
                PartyCount = player.Parties.Count,
                ClearedStageCount = dbContext.StageClearRecords.Count(record =>
                    record.PlayerId == player.Id),
                TotalStageClearCount = dbContext.StageClearRecords
                    .Where(record => record.PlayerId == player.Id)
                    .Sum(record => (int?)record.ClearCount) ?? 0
            })
            .FirstOrDefaultAsync(operationCancellationToken);
    }
}
