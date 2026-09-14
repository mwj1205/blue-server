namespace blueServer.Infrastructure.GameData;

public sealed record ItemCatalogSyncResult(
    int AddedCount,
    int UpdatedCount,
    int DeactivatedCount);
