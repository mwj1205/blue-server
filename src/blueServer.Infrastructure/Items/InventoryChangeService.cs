using blueServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Items;

public sealed class InventoryChangeService
{
    private readonly GameDbContext _db;

    public InventoryChangeService(GameDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryIncreaseResult> IncreaseWithinCurrentTransactionAsync(
        Player player,
        int itemTemplateId,
        int amount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (itemTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemTemplateId),
                itemTemplateId,
                "Item template id must be greater than zero.");
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Item amount must be greater than zero.");
        }

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to change inventory within a parent operation.");
        }

        if (_db.Entry(player).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "The Player must be tracked by the same GameDbContext as the inventory change.");
        }

        var itemTemplate = await _db.ItemTemplates
            .SingleOrDefaultAsync(
                template => template.Id == itemTemplateId,
                cancellationToken);

        if (itemTemplate is null)
        {
            return InventoryIncreaseResult.ItemTemplateNotFound(
                itemTemplateId,
                amount);
        }

        if (!itemTemplate.IsActive)
        {
            return InventoryIncreaseResult.ItemTemplateInactive(
                itemTemplateId,
                amount);
        }

        var playerItem = await _db.PlayerItems
            .Include(item => item.ItemTemplate)
            .SingleOrDefaultAsync(
                item =>
                    item.PlayerId == player.Id &&
                    item.ItemTemplateId == itemTemplateId,
                cancellationToken);

        if (playerItem is null)
        {
            var appliedQuantity = Math.Min(
                amount,
                itemTemplate.MaxQuantity);
            var overflowQuantity = amount - appliedQuantity;

            playerItem = PlayerItem.Create(
                player.Id,
                itemTemplate,
                appliedQuantity);
            _db.PlayerItems.Add(playerItem);

            return InventoryIncreaseResult.Increased(
                itemTemplateId,
                amount,
                appliedQuantity,
                overflowQuantity,
                playerItem.Quantity);
        }

        var increase = playerItem.IncreaseUpToLimit(amount);

        return InventoryIncreaseResult.Increased(
            itemTemplateId,
            increase.RequestedQuantity,
            increase.AppliedQuantity,
            increase.OverflowQuantity,
            increase.QuantityAfter);
    }
}

public enum InventoryIncreaseStatus
{
    Increased = 0,
    ItemTemplateNotFound = 1,
    ItemTemplateInactive = 2
}

public sealed record InventoryIncreaseResult(
    InventoryIncreaseStatus Status,
    int ItemTemplateId,
    int RequestedQuantity,
    int AppliedQuantity,
    int OverflowQuantity,
    int QuantityAfter)
{
    public bool IsSuccess => Status == InventoryIncreaseStatus.Increased;
    public bool HasOverflow => OverflowQuantity > 0;

    public static InventoryIncreaseResult Increased(
        int itemTemplateId,
        int requestedQuantity,
        int appliedQuantity,
        int overflowQuantity,
        int quantityAfter)
    {
        return new InventoryIncreaseResult(
            InventoryIncreaseStatus.Increased,
            itemTemplateId,
            requestedQuantity,
            appliedQuantity,
            overflowQuantity,
            quantityAfter);
    }

    public static InventoryIncreaseResult ItemTemplateNotFound(
        int itemTemplateId,
        int requestedQuantity)
    {
        return Failure(
            InventoryIncreaseStatus.ItemTemplateNotFound,
            itemTemplateId,
            requestedQuantity);
    }

    public static InventoryIncreaseResult ItemTemplateInactive(
        int itemTemplateId,
        int requestedQuantity)
    {
        return Failure(
            InventoryIncreaseStatus.ItemTemplateInactive,
            itemTemplateId,
            requestedQuantity);
    }

    private static InventoryIncreaseResult Failure(
        InventoryIncreaseStatus status,
        int itemTemplateId,
        int requestedQuantity)
    {
        return new InventoryIncreaseResult(
            status,
            itemTemplateId,
            requestedQuantity,
            0,
            0,
            0);
    }
}
