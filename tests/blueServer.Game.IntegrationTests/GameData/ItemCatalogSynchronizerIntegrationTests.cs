using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure;
using blueServer.Infrastructure.GameData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.GameData;

public sealed class ItemCatalogSynchronizerIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task SynchronizeAsync_AppliesCatalogChangesIdempotently()
    {
        var options = CreateOptions();

        await using var db = new GameDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var baseline = await LoadActiveDefinitionsAsync(db);
        var firstId = await CreateUnusedIdAsync(db);
        var secondId = await CreateUnusedIdAsync(db, firstId);
        var suffix = Guid.NewGuid().ToString("N");
        var first = CreateDefinition(
            firstId,
            $"integration_material_{suffix}",
            ItemType.Material);
        var second = CreateDefinition(
            secondId,
            $"integration_consumable_{suffix}",
            ItemType.Consumable);
        var synchronizer = new ItemCatalogSynchronizer(db);
        var initialCatalog = CreateCatalog(baseline, first, second);

        var added = await synchronizer.SynchronizeAsync(initialCatalog);
        var unchanged = await synchronizer.SynchronizeAsync(initialCatalog);

        Assert.Equal(new ItemCatalogSyncResult(2, 0, 0), added);
        Assert.Equal(new ItemCatalogSyncResult(0, 0, 0), unchanged);

        var updatedFirst = first with
        {
            NameKey = $"{first.NameKey}.updated",
            DescriptionKey = $"{first.DescriptionKey}.updated",
            Type = ItemType.EventCurrency
        };
        var withoutSecond = CreateCatalog(baseline, updatedFirst);

        var updatedAndDeactivated = await synchronizer.SynchronizeAsync(
            withoutSecond);

        Assert.Equal(
            new ItemCatalogSyncResult(0, 1, 1),
            updatedAndDeactivated);

        db.ChangeTracker.Clear();

        var deactivated = await db.ItemTemplates
            .AsNoTracking()
            .SingleAsync(template => template.Id == secondId);

        Assert.False(deactivated.IsActive);

        var reactivated = await synchronizer.SynchronizeAsync(
            CreateCatalog(baseline, updatedFirst, second));

        Assert.Equal(new ItemCatalogSyncResult(0, 1, 0), reactivated);

        db.ChangeTracker.Clear();

        var persistedFirst = await db.ItemTemplates
            .AsNoTracking()
            .SingleAsync(template => template.Id == firstId);
        var persistedSecond = await db.ItemTemplates
            .AsNoTracking()
            .SingleAsync(template => template.Id == secondId);

        Assert.Equal(first.Code, persistedFirst.Code);
        Assert.Equal(updatedFirst.NameKey, persistedFirst.NameKey);
        Assert.Equal(
            updatedFirst.DescriptionKey,
            persistedFirst.DescriptionKey);
        Assert.Equal(ItemType.EventCurrency, persistedFirst.Type);
        Assert.True(persistedFirst.IsActive);
        Assert.True(persistedSecond.IsActive);
    }

    [PostgreSqlIntegrationFact]
    public async Task SynchronizeAsync_RejectsStableIdentityChanges()
    {
        var options = CreateOptions();

        await using var db = new GameDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var baseline = await LoadActiveDefinitionsAsync(db);
        var originalId = await CreateUnusedIdAsync(db);
        var reassignedId = await CreateUnusedIdAsync(db, originalId);
        var suffix = Guid.NewGuid().ToString("N");
        var original = CreateDefinition(
            originalId,
            $"integration_identity_{suffix}",
            ItemType.Material);
        var synchronizer = new ItemCatalogSynchronizer(db);

        await synchronizer.SynchronizeAsync(
            CreateCatalog(baseline, original));

        var changedCode = original with
        {
            Code = $"integration_changed_{suffix}"
        };

        var codeChangeException = await Assert.ThrowsAsync<InvalidDataException>(
            () => synchronizer.SynchronizeAsync(
                CreateCatalog(baseline, changedCode)));

        Assert.Contains("cannot change code", codeChangeException.Message);

        var reassigned = original with
        {
            Id = reassignedId
        };

        var reassignmentException = await Assert.ThrowsAsync<InvalidDataException>(
            () => synchronizer.SynchronizeAsync(
                CreateCatalog(baseline, reassigned)));

        Assert.Contains("cannot be reassigned", reassignmentException.Message);

        db.ChangeTracker.Clear();

        var persisted = await db.ItemTemplates
            .AsNoTracking()
            .SingleAsync(template => template.Id == originalId);

        Assert.Equal(original.Code, persisted.Code);
        Assert.True(persisted.IsActive);
        Assert.False(await db.ItemTemplates
            .AsNoTracking()
            .AnyAsync(template => template.Id == reassignedId));
    }

    private static ItemCatalog CreateCatalog(
        IReadOnlyList<ItemTemplateDefinition> baseline,
        params ItemTemplateDefinition[] testDefinitions)
    {
        return new ItemCatalog(
            ItemCatalogJsonLoader.SupportedSchemaVersion,
            [.. baseline, .. testDefinitions]);
    }

    private static ItemTemplateDefinition CreateDefinition(
        int id,
        string code,
        ItemType type)
    {
        return new ItemTemplateDefinition(
            id,
            code,
            $"item.{code}.name",
            $"item.{code}.description",
            type,
            ItemTemplate.InventoryMaxQuantity);
    }

    private static async Task<IReadOnlyList<ItemTemplateDefinition>>
        LoadActiveDefinitionsAsync(GameDbContext db)
    {
        return await db.ItemTemplates
            .AsNoTracking()
            .Where(template => template.IsActive)
            .OrderBy(template => template.Id)
            .Select(template => new ItemTemplateDefinition(
                template.Id,
                template.Code,
                template.NameKey,
                template.DescriptionKey,
                template.Type,
                template.MaxQuantity))
            .ToArrayAsync();
    }

    private static async Task<int> CreateUnusedIdAsync(
        GameDbContext db,
        int? excludedId = null)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Random.Shared.Next(1, int.MaxValue);

            if (candidate != excludedId &&
                !await db.ItemTemplates.AnyAsync(
                    template => template.Id == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "An unused ItemTemplate id could not be generated.");
    }

    private static DbContextOptions<GameDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}
