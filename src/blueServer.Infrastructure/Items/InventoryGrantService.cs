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

        var requestId = request.Items[0].RequestId;
        var payloadHash = InventoryGrantPayloadHasher.Compute(request);
        var existingRecord = await _db.InventoryGrantRecords
            .AsNoTracking()
            .Include(record => record.Items)
            .SingleOrDefaultAsync(
                record =>
                    record.PlayerId == player.Id &&
                    record.RequestId == requestId,
                cancellationToken);

        if (existingRecord is not null)
        {
            return existingRecord.HasSamePayload(payloadHash)
                ? CreateAlreadyGrantedResult(existingRecord, request)
                : InventoryGrantResult.IdempotencyConflict();
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
            AddGrantRecord(
                player.Id,
                requestId,
                payloadHash,
                inventoryResults,
                mailId: null);
            await _db.SaveChangesAsync(cancellationToken);

            return InventoryGrantResult.Granted(inventoryResults);
        }

        var mailResult = await _mailDeliveryService
            .DeliverWithinCurrentTransactionAsync(
                mailRequest,
                cancellationToken);

        if (mailResult.Status != MailDeliveryStatus.Delivered)
        {
            return InventoryGrantResult.OverflowMailRejected(
                inventoryResults,
                mailResult);
        }

        AddGrantRecord(
            player.Id,
            requestId,
            payloadHash,
            inventoryResults,
            mailResult.MailId);
        await _db.SaveChangesAsync(cancellationToken);

        return InventoryGrantResult.Granted(
            inventoryResults,
            mailResult);
    }

    private void AddGrantRecord(
        long playerId,
        Guid requestId,
        string payloadHash,
        IReadOnlyList<InventoryIncreaseResult> inventoryResults,
        long? mailId)
    {
        var snapshots = inventoryResults.Select(result =>
            new InventoryGrantItemSnapshot(
                result.ItemTemplateId,
                result.RequestedQuantity,
                result.AppliedQuantity,
                result.OverflowQuantity,
                result.QuantityAfter));

        _db.InventoryGrantRecords.Add(InventoryGrantRecord.Create(
            playerId,
            requestId,
            payloadHash,
            DateTime.UtcNow,
            mailId,
            snapshots));
    }

    private static InventoryGrantResult CreateAlreadyGrantedResult(
        InventoryGrantRecord record,
        InventoryGrantRequest request)
    {
        var storedItems = record.Items.ToDictionary(
            item => item.ItemTemplateId);
        var inventoryResults = request.Items
            .Select(item =>
            {
                var storedItem = storedItems[item.ItemTemplateId];

                return InventoryIncreaseResult.Increased(
                    storedItem.ItemTemplateId,
                    storedItem.RequestedQuantity,
                    storedItem.AppliedQuantity,
                    storedItem.OverflowQuantity,
                    storedItem.QuantityAfter);
            })
            .ToArray();
        var mailResult = record.MailId.HasValue
            ? MailDeliveryResult.AlreadyDelivered(record.MailId.Value)
            : null;

        return InventoryGrantResult.AlreadyGranted(
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

        if (request.Items
            .Select(item => item.ItemTemplateId)
            .Distinct()
            .Count() != request.Items.Count)
        {
            throw new ArgumentException(
                "Inventory grant items must have unique item template ids.",
                nameof(request));
        }

        var requestId = request.Items[0].RequestId;

        if (requestId == Guid.Empty ||
            request.Items.Any(item => item.RequestId != requestId))
        {
            throw new ArgumentException(
                "Inventory grant items must share one non-empty request id.",
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
    OverflowMailRejected = 2,
    AlreadyGranted = 3,
    IdempotencyConflict = 4
}

public sealed record InventoryGrantResult(
    InventoryGrantStatus Status,
    IReadOnlyList<InventoryIncreaseResult> InventoryResults,
    MailDeliveryResult? OverflowMailResult)
{
    public bool IsSuccess => Status is
        InventoryGrantStatus.Granted or
        InventoryGrantStatus.AlreadyGranted;

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

    public static InventoryGrantResult AlreadyGranted(
        IReadOnlyList<InventoryIncreaseResult> inventoryResults,
        MailDeliveryResult? overflowMailResult)
    {
        return new InventoryGrantResult(
            InventoryGrantStatus.AlreadyGranted,
            inventoryResults,
            overflowMailResult);
    }

    public static InventoryGrantResult IdempotencyConflict()
    {
        return new InventoryGrantResult(
            InventoryGrantStatus.IdempotencyConflict,
            [],
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
