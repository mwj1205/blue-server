using blueServer.Domain.Currencies;

namespace blueServer.Domain.Entities;

/// <summary>완료된 재화 상자 사용 결과. 재요청 시 최신 상태나 재추첨 대신 이 스냅샷을 반환한다.</summary>
public sealed class CurrencyBoxUseRecord
{
    public long Id { get; private set; }
    public long PlayerId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid RewardRequestId { get; private set; }
    public int ItemTemplateId { get; private set; }
    public int UsedQuantity { get; private set; }
    public int QuantityAfter { get; private set; }
    public CurrencyType CurrencyType { get; private set; }
    public int GrantedAmount { get; private set; }
    public int CurrentGold { get; private set; }
    public int CurrentGem { get; private set; }
    public DateTime ProcessedAt { get; private set; }

    public Player? Player { get; private set; }
    public ItemTemplate? ItemTemplate { get; private set; }

    private CurrencyBoxUseRecord()
    {
    }

    public static CurrencyBoxUseRecord Create(
        long playerId,
        Guid requestId,
        Guid rewardRequestId,
        int itemTemplateId,
        int usedQuantity,
        int quantityAfter,
        CurrencyType currencyType,
        int grantedAmount,
        int currentGold,
        int currentGem,
        DateTime processedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemTemplateId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(usedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(quantityAfter);
        if ((long)usedQuantity + quantityAfter > ItemTemplate.InventoryMaxQuantity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityAfter), "The quantity before consumption must not exceed the inventory limit.");
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Request id must not be empty.", nameof(requestId));
        }

        if (rewardRequestId == Guid.Empty)
        {
            throw new ArgumentException("Reward request id must not be empty.", nameof(rewardRequestId));
        }

        if (!Enum.IsDefined(currencyType))
        {
            throw new ArgumentOutOfRangeException(nameof(currencyType));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grantedAmount);
        ArgumentOutOfRangeException.ThrowIfNegative(currentGold);
        ArgumentOutOfRangeException.ThrowIfNegative(currentGem);
        var rewardedBalance = currencyType == CurrencyType.Gold ? currentGold : currentGem;
        if (rewardedBalance < grantedAmount)
        {
            throw new ArgumentException("The resulting balance must include the granted amount.", nameof(grantedAmount));
        }

        if (processedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Processing time must use UTC.", nameof(processedAt));
        }

        return new CurrencyBoxUseRecord
        {
            PlayerId = playerId,
            RequestId = requestId,
            RewardRequestId = rewardRequestId,
            ItemTemplateId = itemTemplateId,
            UsedQuantity = usedQuantity,
            QuantityAfter = quantityAfter,
            CurrencyType = currencyType,
            GrantedAmount = grantedAmount,
            CurrentGold = currentGold,
            CurrentGem = currentGem,
            ProcessedAt = processedAt
        };
    }

    public bool HasSameRequest(int itemTemplateId, int usedQuantity) =>
        ItemTemplateId == itemTemplateId && UsedQuantity == usedQuantity;
}
