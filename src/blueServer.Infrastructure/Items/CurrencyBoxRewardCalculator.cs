using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure.GameData;

namespace blueServer.Infrastructure.Items;

/// <summary>DB와 무관한 개당 추첨/합산. 완료된 RequestId의 재요청에는 호출하지 않는다.</summary>
public sealed class CurrencyBoxRewardCalculator
{
    private readonly ICurrencyBoxRandom _random;

    public CurrencyBoxRewardCalculator(ICurrencyBoxRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    public CurrencyReward Calculate(
        CurrencyBoxDefinition definition,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantity, ItemTemplate.InventoryMaxQuantity);

        var rewardType = definition.CurrencyType switch
        {
            CurrencyType.Gold => RewardType.Gold,
            CurrencyType.Gem => RewardType.Gem,
            _ => throw new ArgumentOutOfRangeException(nameof(definition), "Unsupported reward currency.")
        };
        if (definition.MinAmount <= 0 || definition.MaxAmount < definition.MinAmount ||
            !Enum.IsDefined(definition.AmountMode) ||
            (definition.AmountMode == CurrencyBoxAmountMode.Fixed && definition.MinAmount != definition.MaxAmount) ||
            (definition.AmountMode == CurrencyBoxAmountMode.UniformRandom && definition.MinAmount == definition.MaxAmount))
        {
            throw new ArgumentException("Invalid currency box reward definition.", nameof(definition));
        }

        if (definition.AmountMode == CurrencyBoxAmountMode.Fixed)
        {
            return CurrencyReward.Create(rewardType, checked(definition.MinAmount * quantity));
        }

        var total = 0;
        for (var index = 0; index < quantity; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var amount = _random.NextInclusive(definition.MinAmount, definition.MaxAmount);
            if (amount < definition.MinAmount || amount > definition.MaxAmount)
            {
                throw new InvalidOperationException("Random source returned an amount outside the requested range.");
            }

            total = checked(total + amount);
        }

        return CurrencyReward.Create(rewardType, total);
    }
}
