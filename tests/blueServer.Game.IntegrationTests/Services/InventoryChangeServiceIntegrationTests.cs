using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class InventoryChangeServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task IncreaseWithinCurrentTransactionAsync_PersistsQuantityAndReturnsOverflow()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var suffix = Guid.NewGuid().ToString("N");
        long playerId;
        int activeTemplateId;
        int inactiveTemplateId;
        int missingTemplateId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            activeTemplateId = await CreateUnusedTemplateIdAsync(arrangeDb);
            inactiveTemplateId = await CreateUnusedTemplateIdAsync(
                arrangeDb,
                activeTemplateId);
            missingTemplateId = await CreateUnusedTemplateIdAsync(
                arrangeDb,
                activeTemplateId,
                inactiveTemplateId);

            var player = Player.Create(
                $"inventory-change-{suffix}",
                "integration-test");
            var activeTemplate = CreateTemplate(
                activeTemplateId,
                $"integration_active_{suffix}");
            var inactiveTemplate = CreateTemplate(
                inactiveTemplateId,
                $"integration_inactive_{suffix}");
            inactiveTemplate.Deactivate();

            arrangeDb.AddRange(player, activeTemplate, inactiveTemplate);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        await using (var noTransactionDb = new GameDbContext(options))
        {
            var player = await noTransactionDb.Players.SingleAsync(
                candidate => candidate.Id == playerId);
            var service = new InventoryChangeService(noTransactionDb);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.IncreaseWithinCurrentTransactionAsync(
                    player,
                    activeTemplateId,
                    1));
        }

        await using (var createDb = new GameDbContext(options))
        await using (var transaction = await createDb.Database.BeginTransactionAsync())
        {
            var player = await createDb.Players.SingleAsync(
                candidate => candidate.Id == playerId);
            var service = new InventoryChangeService(createDb);

            var created = await service.IncreaseWithinCurrentTransactionAsync(
                player,
                activeTemplateId,
                999_000);
            var inactive = await service.IncreaseWithinCurrentTransactionAsync(
                player,
                inactiveTemplateId,
                10);
            var missing = await service.IncreaseWithinCurrentTransactionAsync(
                player,
                missingTemplateId,
                10);

            Assert.Equal(InventoryIncreaseStatus.Increased, created.Status);
            Assert.Equal(999_000, created.AppliedQuantity);
            Assert.Equal(0, created.OverflowQuantity);
            Assert.Equal(999_000, created.QuantityAfter);
            Assert.Equal(
                InventoryIncreaseStatus.ItemTemplateInactive,
                inactive.Status);
            Assert.Equal(
                InventoryIncreaseStatus.ItemTemplateNotFound,
                missing.Status);

            await createDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var increaseDb = new GameDbContext(options))
        await using (var transaction = await increaseDb.Database.BeginTransactionAsync())
        {
            var player = await increaseDb.Players.SingleAsync(
                candidate => candidate.Id == playerId);
            var service = new InventoryChangeService(increaseDb);

            var increased = await service.IncreaseWithinCurrentTransactionAsync(
                player,
                activeTemplateId,
                1_200);

            Assert.Equal(InventoryIncreaseStatus.Increased, increased.Status);
            Assert.Equal(1_200, increased.RequestedQuantity);
            Assert.Equal(999, increased.AppliedQuantity);
            Assert.Equal(201, increased.OverflowQuantity);
            Assert.Equal(ItemTemplate.InventoryMaxQuantity, increased.QuantityAfter);
            Assert.True(increased.HasOverflow);

            await increaseDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var persistedItem = await assertDb.PlayerItems
                .AsNoTracking()
                .SingleAsync(item =>
                    item.PlayerId == playerId &&
                    item.ItemTemplateId == activeTemplateId);

            Assert.Equal(
                ItemTemplate.InventoryMaxQuantity,
                persistedItem.Quantity);
            Assert.False(await assertDb.PlayerItems.AnyAsync(item =>
                item.PlayerId == playerId &&
                item.ItemTemplateId == inactiveTemplateId));
        }
    }

    private static ItemTemplate CreateTemplate(int id, string code)
    {
        return ItemTemplate.Create(
            id,
            code,
            $"item.{code}.name",
            $"item.{code}.description",
            ItemType.Material);
    }

    private static async Task<int> CreateUnusedTemplateIdAsync(
        GameDbContext db,
        params int[] excludedIds)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Random.Shared.Next(1, int.MaxValue);

            if (!excludedIds.Contains(candidate) &&
                !await db.ItemTemplates.AnyAsync(
                    template => template.Id == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "An unused ItemTemplate id could not be generated.");
    }
}
