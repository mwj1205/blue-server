using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Items;

public sealed class InventoryListQueryService
{
    // 게임의 보유 한도가 아니라 전체 응답의 메모리/전송 부하를 제한하는 보호 상한이다.
    public const int MaxReturnedItems = 10_000;

    private readonly GameDbContext _db;

    public InventoryListQueryService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryListResult> GetAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        cancellationToken.ThrowIfCancellationRequested();
        var playerExists = await _db.Players
            .AsNoTracking()
            .AnyAsync(player => player.Id == playerId, cancellationToken);
        if (!playerExists)
        {
            return new InventoryListResult(InventoryListStatus.PlayerNotFound, []);
        }

        // 목록은 한 SELECT로 고정한다. 이후 전송 프레임마다 DB를 다시 조회하지 않는다.
        // 비활성 정의의 보유 아이템도 반환하며, 조회 자체가 사용을 허용하는 것은 아니다.
        var rows = await _db.PlayerItems
            .AsNoTracking()
            .Where(item => item.PlayerId == playerId && item.Quantity > 0)
            .OrderBy(item => item.ItemTemplateId)
            .Select(item => new InventoryListItem(
                item.ItemTemplateId,
                item.Quantity,
                item.ItemTemplate!.Type,
                item.ItemTemplate!.IsActive))
            .Take(MaxReturnedItems + 1)
            .ToListAsync(cancellationToken);
        if (rows.Count > MaxReturnedItems)
        {
            return new InventoryListResult(InventoryListStatus.InventoryTooLarge, []);
        }

        return new InventoryListResult(InventoryListStatus.Success, rows.AsReadOnly());
    }
}
