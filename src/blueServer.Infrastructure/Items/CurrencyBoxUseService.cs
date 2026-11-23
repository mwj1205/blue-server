using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.GameData;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace blueServer.Infrastructure.Items;

/// <summary>상위 사용 처리. 호출별 Scoped DbContext에서 트랜잭션을 소유한다.</summary>
public sealed class CurrencyBoxUseService
{
    private readonly GameDbContext _db;
    private readonly InventoryChangeService _inventory;
    private readonly RewardGrantService _rewards;
    private readonly CurrencyBoxRewardCalculator _calculator;
    private readonly IReadOnlyDictionary<int, CurrencyBoxDefinition> _boxes;

    public CurrencyBoxUseService(
        GameDbContext db,
        InventoryChangeService inventory,
        RewardGrantService rewards,
        CurrencyBoxRewardCalculator calculator,
        CurrencyBoxCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _db = db;
        _inventory = inventory;
        _rewards = rewards;
        _calculator = calculator;
        _boxes = catalog.Boxes.ToDictionary(box => box.ItemTemplateId);
    }

    public async Task<CurrencyBoxUseResult> UseAsync(
        long playerId,
        CurrencyBoxUseRequest request,
        DateTime processedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (processedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Processing time must use UTC.", nameof(processedAt));
        }

        if (_db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Currency box use must own its transaction.");
        }

        // PostgreSQL timestamp 정밀도에 맞춰 최초 응답과 재응답 시각을 일치시킨다.
        processedAt = new DateTime(processedAt.Ticks - processedAt.Ticks % 10, DateTimeKind.Utc);
        var existing = await FindResultAsync(playerId, request, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        if (!_boxes.TryGetValue(request.ItemTemplateId, out var box))
        {
            return CurrencyBoxUseResult.Failure(CurrencyBoxUseStatus.BoxNotFound);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var player = await _db.Players.SingleOrDefaultAsync(player => player.Id == playerId, cancellationToken);
            if (player is null)
            {
                return await RejectAsync(transaction, playerId, request, CurrencyBoxUseStatus.PlayerNotFound);
            }

            var sourceId = $"currency-box:{request.RequestId:D}";
            var consumed = await _inventory.DecreaseWithinCurrentTransactionAsync(
                player,
                new InventoryDecreaseRequest(
                    [new InventoryDecreaseItem(request.ItemTemplateId, request.Quantity)],
                    InventoryItemChangeReasonType.ItemUseCost,
                    sourceId,
                    request.RequestId,
                    processedAt),
                cancellationToken);
            if (!consumed.IsSuccess)
            {
                var status = consumed.Status switch
                {
                    InventoryDecreaseStatus.InsufficientQuantity => CurrencyBoxUseStatus.InsufficientQuantity,
                    InventoryDecreaseStatus.ItemTemplateNotFound => CurrencyBoxUseStatus.ItemTemplateNotFound,
                    InventoryDecreaseStatus.ItemTemplateInactive => CurrencyBoxUseStatus.ItemTemplateInactive,
                    _ => throw new InvalidOperationException("Unexpected inventory consumption status.")
                };
                return await RejectAsync(transaction, playerId, request, status);
            }

            // 보유량 부족 시 추첨하지 않는다. 완료된 재요청도 앞의 기록 조회에서 반환한다.
            var reward = _calculator.Calculate(box, request.Quantity, cancellationToken);
            var rewardRequestId = Guid.NewGuid();
            var granted = await _rewards.PrepareWithinCurrentTransactionAsync(
                playerId,
                new RewardGrantRequest(
                    rewardRequestId,
                    "Currency box use",
                    RewardBundle.Create([reward]),
                    CurrencyChangeReasonType.ItemUseReward,
                    sourceId),
                cancellationToken);
            if (granted.Status != RewardGrantStatus.Granted)
            {
                throw new InvalidOperationException("A new currency box operation must prepare a new reward grant.");
            }

            var record = CurrencyBoxUseRecord.Create(
                playerId,
                request.RequestId,
                rewardRequestId,
                request.ItemTemplateId,
                request.Quantity,
                consumed.Items.Single().QuantityAfter,
                box.CurrencyType,
                reward.Amount,
                player.Gold,
                player.Gem,
                processedAt);
            _db.CurrencyBoxUseRecords.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CurrencyBoxUseResult.FromRecord(record);
        }
        catch (DbUpdateException exception) when (IsExpectedCollision(exception))
        {
            await RollbackAsync(transaction);
            return await FindResultAsync(playerId, request, CancellationToken.None)
                ?? CurrencyBoxUseResult.Failure(CurrencyBoxUseStatus.ConcurrencyConflict);
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task<CurrencyBoxUseResult?> FindResultAsync(
        long playerId,
        CurrencyBoxUseRequest request,
        CancellationToken cancellationToken)
    {
        var record = await _db.CurrencyBoxUseRecords.AsNoTracking().SingleOrDefaultAsync(
            record => record.PlayerId == playerId && record.RequestId == request.RequestId,
            cancellationToken);
        return record is null
            ? null
            : record.HasSameRequest(request.ItemTemplateId, request.Quantity)
                ? CurrencyBoxUseResult.FromRecord(record)
                : CurrencyBoxUseResult.Failure(CurrencyBoxUseStatus.IdempotencyConflict);
    }

    private async Task<CurrencyBoxUseResult> RejectAsync(
        IDbContextTransaction transaction,
        long playerId,
        CurrencyBoxUseRequest request,
        CurrencyBoxUseStatus status)
    {
        await RollbackAsync(transaction);
        // 최초 조회 후 다른 동일 요청이 완료됐으면 부족 응답 대신 확정 결과를 복원한다.
        return await FindResultAsync(playerId, request, CancellationToken.None)
            ?? CurrencyBoxUseResult.Failure(status);
    }

    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
    }

    private static bool IsExpectedCollision(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException ||
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_CurrencyBoxUseRecords_PlayerId_RequestId" or
                "IX_InventoryItemChangeLogs_PlayerId_RequestId_ItemTemplateId"
        };
}
