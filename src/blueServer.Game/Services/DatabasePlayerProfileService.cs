using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Game.Services;

public sealed class DatabasePlayerProfileService : IPlayerProfileService
{
    private readonly GameDbContext _db;

    public DatabasePlayerProfileService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<PlayerProfileResult> GetAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        var profile = await _db.Players
            .AsNoTracking()
            .Where(player => player.Id == playerId)
            .Select(player => new PlayerProfileResult(
                true,
                "Player profile loaded",
                player.Id,
                player.Nickname,
                player.Gold,
                player.Gem,
                player.OwnedCharacters.Count,
                player.Parties.Count,
                _db.StageClearRecords.Count(record =>
                    record.PlayerId == player.Id),
                _db.StageClearRecords
                    .Where(record => record.PlayerId == player.Id)
                    .Sum(record => (int?)record.ClearCount) ?? 0))
            .FirstOrDefaultAsync(cancellationToken);

        return profile ?? PlayerProfileResult.Fail("Player not found");
    }
}
