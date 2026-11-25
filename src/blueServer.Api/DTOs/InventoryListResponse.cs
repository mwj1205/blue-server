namespace blueServer.Api.DTOs;

public sealed record InventoryListResponse(
    IReadOnlyList<InventoryItemResponse> Items);

public sealed record InventoryItemResponse(
    int ItemTemplateId,
    int Quantity,
    string Type,
    bool IsActive);
