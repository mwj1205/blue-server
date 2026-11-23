using blueServer.Domain.Currencies;

namespace blueServer.Infrastructure.GameData;

public enum CurrencyBoxAmountMode
{
    Fixed = 1,
    UniformRandom = 2
}

public sealed record CurrencyBoxCatalog(int SchemaVersion, IReadOnlyList<CurrencyBoxDefinition> Boxes);

/// <summary>상자 1개당 보상. UniformRandom은 최소·최대값을 포함한 정수 균등 분포다.</summary>
public sealed record CurrencyBoxDefinition(
    int ItemTemplateId,
    CurrencyType CurrencyType,
    CurrencyBoxAmountMode AmountMode,
    int MinAmount,
    int MaxAmount);
