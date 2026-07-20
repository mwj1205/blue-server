using blueServer.Api.DTOs;
using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Api.Services;

public sealed class DatabasePlayerProfileQueryService :
    IPlayerProfileQueryService
{
    private readonly GameDbContext _db;

    public DatabasePlayerProfileQueryService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<PlayerProfileResponse?> GetAsync(
        long playerId,
        CancellationToken cancellationToken)
    {
        return await _db.Players
            .AsNoTracking()
            .Where(player => player.Id == playerId)
            .Select(player => new PlayerProfileResponse
            {
                Id = player.Id,
                Nickname = player.Nickname,
                Gold = player.Gold,
                Gem = player.Gem,
                OwnedCharacterCount = player.OwnedCharacters.Count,
                PartyCount = player.Parties.Count,
                ClearedStageCount = _db.StageClearRecords.Count(record =>
                    record.PlayerId == player.Id),
                TotalStageClearCount = _db.StageClearRecords
                    .Where(record => record.PlayerId == player.Id)
                    .Sum(record => (int?)record.ClearCount) ?? 0
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
