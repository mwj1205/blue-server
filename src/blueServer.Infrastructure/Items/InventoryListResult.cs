using blueServer.Domain.Items;

namespace blueServer.Infrastructure.Items;

public sealed record InventoryListItem(
    int ItemTemplateId,
    int Quantity,
    ItemType Type,
    bool IsActive);

public enum InventoryListStatus
{
    Success = 0,
    PlayerNotFound = 1,
    InventoryTooLarge = 2
}

public sealed record InventoryListResult(
    InventoryListStatus Status,
    IReadOnlyList<InventoryListItem> Items)
{
    public bool IsSuccess => Status == InventoryListStatus.Success;
}
