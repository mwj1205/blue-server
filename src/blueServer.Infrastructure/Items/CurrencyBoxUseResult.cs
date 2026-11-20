using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;

namespace blueServer.Infrastructure.Items;

public sealed record CurrencyBoxUseRequest
{
    public int ItemTemplateId { get; }
    public int Quantity { get; }
    public Guid RequestId { get; }

    public CurrencyBoxUseRequest(int itemTemplateId, int quantity, Guid requestId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemTemplateId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantity, ItemTemplate.InventoryMaxQuantity);
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        ItemTemplateId = itemTemplateId;
        Quantity = quantity;
        RequestId = requestId;
    }
}

public enum CurrencyBoxUseStatus
{
    Used,
    PlayerNotFound,
    BoxNotFound,
    InsufficientQuantity,
    ItemTemplateNotFound,
    ItemTemplateInactive,
    IdempotencyConflict,
    ConcurrencyConflict
}

public sealed record CurrencyBoxUseSnapshot(
    Guid RequestId,
    int ItemTemplateId,
    int UsedQuantity,
    int QuantityAfter,
    CurrencyType CurrencyType,
    int GrantedAmount,
    int CurrentGold,
    int CurrentGem,
    DateTime ProcessedAt);

public sealed record CurrencyBoxUseResult(CurrencyBoxUseStatus Status, CurrencyBoxUseSnapshot? Snapshot)
{
    public bool IsSuccess => Status == CurrencyBoxUseStatus.Used;

    internal static CurrencyBoxUseResult FromRecord(CurrencyBoxUseRecord record) => new(
        CurrencyBoxUseStatus.Used,
        new CurrencyBoxUseSnapshot(
            record.RequestId, record.ItemTemplateId, record.UsedQuantity, record.QuantityAfter,
            record.CurrencyType, record.GrantedAmount, record.CurrentGold, record.CurrentGem, record.ProcessedAt));

    internal static CurrencyBoxUseResult Failure(CurrencyBoxUseStatus status) => new(status, null);
}
