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

    public async Task<InventoryGrantResult> GrantAsync(
        long playerId,
        InventoryGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(playerId),
                playerId,
                "Player id must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var payloadHash = InventoryGrantPayloadHasher.Compute(request);
        var existingResult = await TryResolveExistingResultAsync(
            playerId,
            request,
            payloadHash,
            cancellationToken);

        if (existingResult is not null)
        {
            return existingResult;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            var player = await _db.Players.SingleOrDefaultAsync(
                player => player.Id == playerId,
                cancellationToken);

            if (player is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return InventoryGrantResult.PlayerNotFound();
            }

            var result = await GrantWithinCurrentTransactionAsync(
                player,
                request,
                cancellationToken);

            if (result.Status == InventoryGrantStatus.Granted)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            var concurrentResult = await TryResolveExistingResultAsync(
                playerId,
                request,
                payloadHash,
                CancellationToken.None);

            return concurrentResult ??
                InventoryGrantResult.ConcurrencyConflict();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            // Unique Constraint 경합이면 먼저 완료된 동일 요청 결과로 복구
            var concurrentResult = await TryResolveExistingResultAsync(
                playerId,
                request,
                payloadHash,
                CancellationToken.None);

            if (concurrentResult is not null)
            {
                return concurrentResult;
            }

            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
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
        var existingResult = await TryResolveExistingResultAsync(
            player.Id,
            request,
            payloadHash,
            cancellationToken);

        if (existingResult is not null)
        {
            return existingResult;
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

    private async Task<InventoryGrantResult?> TryResolveExistingResultAsync(
        long playerId,
        InventoryGrantRequest request,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var requestId = request.Items[0].RequestId;
        var existingRecord = await _db.InventoryGrantRecords
            .AsNoTracking()
            .Include(record => record.Items)
            .SingleOrDefaultAsync(
                record =>
                    record.PlayerId == playerId &&
                    record.RequestId == requestId,
                cancellationToken);

        if (existingRecord is null)
        {
            return null;
        }

        return existingRecord.HasSamePayload(payloadHash)
            ? CreateAlreadyGrantedResult(existingRecord, request)
            : InventoryGrantResult.IdempotencyConflict();
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
    IdempotencyConflict = 4,
    PlayerNotFound = 5,
    ConcurrencyConflict = 6
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

    public static InventoryGrantResult PlayerNotFound()
    {
        return Failure(InventoryGrantStatus.PlayerNotFound);
    }

    public static InventoryGrantResult ConcurrencyConflict()
    {
        return Failure(InventoryGrantStatus.ConcurrencyConflict);
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

    private static InventoryGrantResult Failure(
        InventoryGrantStatus status)
    {
        return new InventoryGrantResult(status, [], null);
    }
}
