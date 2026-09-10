namespace blueServer.Domain.Items;

public readonly record struct ItemIncreaseResult(
    int RequestedQuantity,
    int AppliedQuantity,
    int OverflowQuantity,
    int QuantityAfter)
{
    public bool HasOverflow => OverflowQuantity > 0;
}
