using blueServer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace blueServer.Infrastructure.GameData;

public sealed class ItemCatalogSynchronizer
{
    private readonly GameDbContext _db;

    public ItemCatalogSynchronizer(GameDbContext db)
    {
        _db = db;
    }

    public async Task<ItemCatalogSyncResult> SynchronizeAsync(
        ItemCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        ValidateCatalog(catalog);

        var existingTemplates = await _db.ItemTemplates
            .ToListAsync(cancellationToken);
        var existingById = existingTemplates.ToDictionary(
            template => template.Id);
        var existingByCode = existingTemplates.ToDictionary(
            template => template.Code,
            StringComparer.Ordinal);
        var catalogIds = catalog.Templates
            .Select(definition => definition.Id)
            .ToHashSet();

        var addedCount = 0;
        var updatedCount = 0;
        var deactivatedCount = 0;

        foreach (var definition in catalog.Templates)
        {
            if (existingById.TryGetValue(definition.Id, out var existing))
            {
                EnsureCodeIsUnchanged(existing, definition);

                if (existing.UpdateDefinition(
                        definition.NameKey,
                        definition.DescriptionKey,
                        definition.Type))
                {
                    updatedCount++;
                }

                continue;
            }

            EnsureCodeIsNotReassigned(existingByCode, definition);

            _db.ItemTemplates.Add(ItemTemplate.Create(
                definition.Id,
                definition.Code,
                definition.NameKey,
                definition.DescriptionKey,
                definition.Type));
            addedCount++;
        }

        foreach (var existing in existingTemplates)
        {
            if (!catalogIds.Contains(existing.Id) && existing.Deactivate())
            {
                deactivatedCount++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ItemCatalogSyncResult(
            addedCount,
            updatedCount,
            deactivatedCount);
    }

    private static void ValidateCatalog(ItemCatalog catalog)
    {
        if (catalog.Templates.Count == 0)
        {
            throw new InvalidDataException(
                "Item catalog must contain at least one item.");
        }

        var ids = new HashSet<int>();
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in catalog.Templates)
        {
            if (!ids.Add(definition.Id))
            {
                throw new InvalidDataException(
                    $"Item catalog contains duplicate id {definition.Id}.");
            }

            if (!codes.Add(definition.Code))
            {
                throw new InvalidDataException(
                    $"Item catalog contains duplicate code '{definition.Code}'.");
            }

            if (definition.MaxQuantity != ItemTemplate.InventoryMaxQuantity)
            {
                throw new InvalidDataException(
                    $"Item catalog entry {definition.Id} has unsupported max quantity " +
                    $"{definition.MaxQuantity}.");
            }
        }
    }

    private static void EnsureCodeIsUnchanged(
        ItemTemplate existing,
        ItemTemplateDefinition definition)
    {
        if (!string.Equals(
                existing.Code,
                definition.Code,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Item catalog entry {definition.Id} cannot change code " +
                $"from '{existing.Code}' to '{definition.Code}'.");
        }
    }

    private static void EnsureCodeIsNotReassigned(
        IReadOnlyDictionary<string, ItemTemplate> existingByCode,
        ItemTemplateDefinition definition)
    {
        if (existingByCode.TryGetValue(definition.Code, out var existing))
        {
            throw new InvalidDataException(
                $"Item catalog code '{definition.Code}' is already assigned " +
                $"to item {existing.Id} and cannot be reassigned to " +
                $"item {definition.Id}.");
        }
    }
}
