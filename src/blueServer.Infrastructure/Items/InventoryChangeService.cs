using blueServer.Domain.Entities;
using blueServer.Domain.Items;
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
        InventoryIncreaseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

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
                template => template.Id == request.ItemTemplateId,
                cancellationToken);

        if (itemTemplate is null)
        {
            return InventoryIncreaseResult.ItemTemplateNotFound(
                request.ItemTemplateId,
                request.Amount);
        }

        if (!itemTemplate.IsActive)
        {
            return InventoryIncreaseResult.ItemTemplateInactive(
                request.ItemTemplateId,
                request.Amount);
        }

        var playerItem = await _db.PlayerItems
            .Include(item => item.ItemTemplate)
            .SingleOrDefaultAsync(
                item =>
                    item.PlayerId == player.Id &&
                    item.ItemTemplateId == request.ItemTemplateId,
                cancellationToken);

        if (playerItem is null)
        {
            var appliedQuantity = Math.Min(
                request.Amount,
                itemTemplate.MaxQuantity);
            var overflowQuantity = request.Amount - appliedQuantity;

            playerItem = PlayerItem.Create(
                player.Id,
                itemTemplate,
                appliedQuantity);
            _db.PlayerItems.Add(playerItem);
            AddChange(
                player,
                request,
                appliedQuantity,
                quantityBefore: 0);

            return InventoryIncreaseResult.Increased(
                request.ItemTemplateId,
                request.Amount,
                appliedQuantity,
                overflowQuantity,
                playerItem.Quantity);
        }

        var quantityBefore = playerItem.Quantity;
        var increase = playerItem.IncreaseUpToLimit(request.Amount);
        AddChange(
            player,
            request,
            increase.AppliedQuantity,
            quantityBefore);

        return InventoryIncreaseResult.Increased(
            request.ItemTemplateId,
            increase.RequestedQuantity,
            increase.AppliedQuantity,
            increase.OverflowQuantity,
            increase.QuantityAfter);
    }

    private void AddChange(
        Player player,
        InventoryIncreaseRequest request,
        int appliedQuantity,
        int quantityBefore)
    {
        if (appliedQuantity == 0)
        {
            return;
        }

        var change = InventoryItemChangeLog.Create(
            player.Id,
            request.ItemTemplateId,
            appliedQuantity,
            quantityBefore,
            request.ReasonType,
            request.SourceId,
            request.RequestId,
            request.ChangedAt);

        _db.InventoryItemChangeLogs.Add(change);
    }

    private static void ValidateRequest(InventoryIncreaseRequest request)
    {
        if (request.ItemTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.ItemTemplateId,
                "Item template id must be greater than zero.");
        }

        if (request.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Amount,
                "Item amount must be greater than zero.");
        }

        if (!Enum.IsDefined(request.ReasonType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.ReasonType,
                "Inventory item change reason type is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceId))
        {
            throw new ArgumentException(
                "Inventory item change source id is required.",
                nameof(request));
        }

        if (request.SourceId.Trim().Length >
            InventoryItemChangeLog.MaxSourceIdLength)
        {
            throw new ArgumentException(
                $"Inventory item change source id must not exceed {InventoryItemChangeLog.MaxSourceIdLength} characters.",
                nameof(request));
        }

        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id must not be empty.",
                nameof(request));
        }

        if (request.ChangedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Inventory item change time must use UTC.",
                nameof(request));
        }
    }
}

public sealed record InventoryIncreaseRequest(
    int ItemTemplateId,
    int Amount,
    InventoryItemChangeReasonType ReasonType,
    string SourceId,
    Guid RequestId,
    DateTime ChangedAt);

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
