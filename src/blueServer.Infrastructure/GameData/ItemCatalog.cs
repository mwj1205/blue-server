using blueServer.Domain.Items;

namespace blueServer.Infrastructure.GameData;

public sealed record ItemCatalog(
    int SchemaVersion,
    IReadOnlyList<ItemTemplateDefinition> Templates);

public sealed record ItemTemplateDefinition(
    int Id,
    string Code,
    string NameKey,
    string DescriptionKey,
    ItemType Type,
    int MaxQuantity);
