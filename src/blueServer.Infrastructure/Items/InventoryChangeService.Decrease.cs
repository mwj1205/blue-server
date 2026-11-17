using blueServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Items;

public sealed partial class InventoryChangeService
{
    /// <summary>
    /// 전체 비용을 검증한 뒤 추적 객체와 이력만 변경한다. 저장/Commit과 작업 멱등성은 호출자 책임이다.
    /// 예외 또는 저장 시 동시성 충돌이 발생하면 호출자는 상위 작업 전체를 롤백해야 한다.
    /// </summary>
    public async Task<InventoryDecreaseResult> DecreaseWithinCurrentTransactionAsync(
        Player player,
        InventoryDecreaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An active transaction is required to consume inventory.");
        }

        if (player.Id <= 0 || _db.Entry(player).State is EntityState.Detached or EntityState.Deleted)
        {
            throw new InvalidOperationException("The Player must be tracked by the same GameDbContext.");
        }

        var ids = request.Items.Select(item => item.ItemTemplateId).ToArray();
        var templates = await _db.ItemTemplates.Where(template => ids.Contains(template.Id))
            .ToDictionaryAsync(template => template.Id, cancellationToken);
        foreach (var cost in request.Items)
        {
            if (!templates.TryGetValue(cost.ItemTemplateId, out var template))
            {
                return Failure(InventoryDecreaseStatus.ItemTemplateNotFound, cost, 0);
            }

            if (!template.IsActive)
            {
                return Failure(InventoryDecreaseStatus.ItemTemplateInactive, cost, 0);
            }
        }

        // 앞선 지급/소비로 바뀐 추적 객체와 아직 저장되지 않은 신규 행도 사용한다.
        var items = _db.PlayerItems.Local
            .Where(item => item.PlayerId == player.Id && ids.Contains(item.ItemTemplateId))
            .ToDictionary(item => item.ItemTemplateId);
        var missingIds = ids.Where(id => !items.ContainsKey(id)).ToArray();
        if (missingIds.Length > 0)
        {
            var storedItems = await _db.PlayerItems
                .Where(item => item.PlayerId == player.Id && missingIds.Contains(item.ItemTemplateId))
                .ToArrayAsync(cancellationToken);
            foreach (var item in storedItems)
            {
                items.Add(item.ItemTemplateId, item);
            }
        }

        foreach (var cost in request.Items)
        {
            items.TryGetValue(cost.ItemTemplateId, out var item);
            if (item is not null && _db.Entry(item).State == EntityState.Deleted)
            {
                throw new InvalidOperationException("An inventory row pending deletion cannot be consumed.");
            }

            var available = item?.Quantity ?? 0;
            if (available < cost.Amount)
            {
                return Failure(InventoryDecreaseStatus.InsufficientQuantity, cost, available);
            }
        }

        // 예상 가능한 거절은 위에서 전부 확인했다. 이후 실패는 반환하지 않고 상위 롤백으로 처리한다.
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<InventoryDecreaseItemResult>(request.Items.Count);
        foreach (var cost in request.Items)
        {
            var item = items[cost.ItemTemplateId];
            var before = item.Quantity;
            var change = InventoryItemChangeLog.Create(
                player.Id,
                cost.ItemTemplateId,
                -cost.Amount,
                before,
                request.ReasonType,
                request.SourceId,
                request.RequestId,
                request.ChangedAt);
            if (!item.TryDecrease(cost.Amount))
            {
                throw new InvalidOperationException("Inventory changed after the available quantity was checked.");
            }

            _db.InventoryItemChangeLogs.Add(change);
            results.Add(new InventoryDecreaseItemResult(cost.ItemTemplateId, cost.Amount, item.Quantity));
        }

        return new InventoryDecreaseResult(
            InventoryDecreaseStatus.Decreased, null, 0, 0, results.AsReadOnly());
    }

    private static InventoryDecreaseResult Failure(
        InventoryDecreaseStatus status,
        InventoryDecreaseItem cost,
        int availableQuantity)
    {
        return new InventoryDecreaseResult(
            status, cost.ItemTemplateId, cost.Amount, availableQuantity,
            Array.Empty<InventoryDecreaseItemResult>());
    }
}
