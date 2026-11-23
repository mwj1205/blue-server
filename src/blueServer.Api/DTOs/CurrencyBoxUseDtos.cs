namespace blueServer.Api.DTOs;

public sealed record CurrencyBoxUseBody(int Quantity, Guid RequestId);

public sealed record CurrencyBoxUseResponse(
    Guid RequestId,
    int ItemTemplateId,
    int UsedQuantity,
    int QuantityAfter,
    string CurrencyType,
    int GrantedAmount,
    int CurrentGold,
    int CurrentGem,
    DateTime ProcessedAt);
