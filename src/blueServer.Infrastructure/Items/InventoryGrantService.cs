using blueServer.Domain.Entities;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.Items;

public sealed class InventoryGrantService
{
    public const int MaxItemCount = 100;

    private readonly GameDbContext _db;
    private readonly InventoryChangeService _inventoryChangeService;
    private readonly MailDeliveryService _mailDeliveryService;

    public InventoryGrantService(
        GameDbContext db,
        InventoryChangeService inventoryChangeService,
        MailDeliveryService mailDeliveryService)
    {
        _db = db;
        _inventoryChangeService = inventoryChangeService;
        _mailDeliveryService = mailDeliveryService;
    }

    public async Task<InventoryGrantResult> GrantWithinCurrentTransactionAsync(
        Player player,
        InventoryGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "An active transaction is required to grant inventory within a parent operation.");
        }

        if (_db.Entry(player).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "The Player must be tracked by the same GameDbContext as the inventory grant.");
        }

        var inventoryResults = new List<InventoryIncreaseResult>(
            request.Items.Count);

        foreach (var item in request.Items)
        {
            var result = await _inventoryChangeService
                .IncreaseWithinCurrentTransactionAsync(
                    player,
                    item,
                    cancellationToken);

            inventoryResults.Add(result);

            if (!result.IsSuccess)
            {
                return InventoryGrantResult.InventoryRejected(
                    inventoryResults);
            }
        }

        var mailRequest = InventoryOverflowMailRequestFactory.Create(
            player.Id,
            inventoryResults,
            request.OverflowMail);

        if (mailRequest is null)
        {
            await _db.SaveChangesAsync(cancellationToken);

            return InventoryGrantResult.Granted(inventoryResults);
        }

        var mailResult = await _mailDeliveryService
            .DeliverWithinCurrentTransactionAsync(
                mailRequest,
                cancellationToken);

        return mailResult.Status == MailDeliveryStatus.Delivered
            ? InventoryGrantResult.Granted(
                inventoryResults,
                mailResult)
            : InventoryGrantResult.OverflowMailRejected(
                inventoryResults,
                mailResult);
    }

    private static void ValidateRequest(InventoryGrantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentNullException.ThrowIfNull(request.OverflowMail);

        if (request.Items.Count == 0)
        {
            throw new ArgumentException(
                "At least one inventory item is required.",
                nameof(request));
        }

        if (request.Items.Count > MaxItemCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Items.Count,
                $"Inventory grant must not exceed {MaxItemCount} items.");
        }

        if (request.Items.Any(item => item is null))
        {
            throw new ArgumentException(
                "Inventory grant items must not contain null.",
                nameof(request));
        }
    }
}

public sealed record InventoryGrantRequest(
    IReadOnlyList<InventoryIncreaseRequest> Items,
    InventoryOverflowMailOptions OverflowMail);

public enum InventoryGrantStatus
{
    Granted = 0,
    InventoryRejected = 1,
    OverflowMailRejected = 2
}

public sealed record InventoryGrantResult(
    InventoryGrantStatus Status,
    IReadOnlyList<InventoryIncreaseResult> InventoryResults,
    MailDeliveryResult? OverflowMailResult)
{
    public bool IsSuccess => Status == InventoryGrantStatus.Granted;

    public static InventoryGrantResult Granted(
        IReadOnlyList<InventoryIncreaseResult> inventoryResults,
        MailDeliveryResult? overflowMailResult = null)
    {
        return new InventoryGrantResult(
            InventoryGrantStatus.Granted,
            inventoryResults,
            overflowMailResult);
    }

    public static InventoryGrantResult InventoryRejected(
        IReadOnlyList<InventoryIncreaseResult> inventoryResults)
    {
        return new InventoryGrantResult(
            InventoryGrantStatus.InventoryRejected,
            inventoryResults,
            null);
    }

    public static InventoryGrantResult OverflowMailRejected(
        IReadOnlyList<InventoryIncreaseResult> inventoryResults,
        MailDeliveryResult overflowMailResult)
    {
        return new InventoryGrantResult(
            InventoryGrantStatus.OverflowMailRejected,
            inventoryResults,
            overflowMailResult);
    }
}
