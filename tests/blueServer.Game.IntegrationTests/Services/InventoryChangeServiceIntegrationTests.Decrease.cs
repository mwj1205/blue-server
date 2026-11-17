using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed partial class InventoryChangeServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task Decrease_ValidatesAllCostsBeforeMutationAndLeavesCommitToCaller()
    {
        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!)
            .Options;
        var suffix = Guid.NewGuid().ToString("N");
        long playerId;
        int firstId;
        int secondId;
        await using (var db = new GameDbContext(options))
        {
            firstId = await CreateUnusedTemplateIdAsync(db);
            secondId = await CreateUnusedTemplateIdAsync(db, firstId);
            var player = Player.Create($"inventory-consumption-{suffix}");
            db.AddRange(player, CreateTemplate(firstId, $"consume_a_{suffix}"),
                CreateTemplate(secondId, $"consume_b_{suffix}"));
            await db.SaveChangesAsync();
            playerId = player.Id;
        }

        var requestId = Guid.NewGuid();
        await using (var db = new GameDbContext(options))
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var player = await db.Players.SingleAsync(player => player.Id == playerId);
            var service = new InventoryChangeService(db);
            // 같은 Transaction에서 아직 저장하지 않은 지급량도 소비할 수 있다.
            await service.IncreaseWithinCurrentTransactionAsync(player, CreateRequest(firstId, 10, suffix));
            await service.IncreaseWithinCurrentTransactionAsync(player, CreateRequest(secondId, 5, suffix));
            var rejected = await service.DecreaseWithinCurrentTransactionAsync(player,
                new InventoryDecreaseRequest([new(firstId, 7), new(secondId, 6)],
                    InventoryItemChangeReasonType.AdminAdjustment, suffix, Guid.NewGuid(), DateTime.UtcNow));
            Assert.Equal(InventoryDecreaseStatus.InsufficientQuantity, rejected.Status);
            Assert.Equal(secondId, rejected.FailedItemTemplateId);
            Assert.Equal(5, rejected.AvailableQuantity);
            Assert.Equal(10, db.PlayerItems.Local.Single(item => item.ItemTemplateId == firstId).Quantity);
            Assert.DoesNotContain(db.InventoryItemChangeLogs.Local, log => log.Delta < 0);

            var consumed = await service.DecreaseWithinCurrentTransactionAsync(player,
                new InventoryDecreaseRequest([new(firstId, 3), new(firstId, 7), new(secondId, 2)],
                    InventoryItemChangeReasonType.AdminAdjustment, suffix, requestId, DateTime.UtcNow));
            Assert.True(consumed.IsSuccess);
            Assert.Equal(2, consumed.Items.Count);
            Assert.Equal(EntityState.Added, db.Entry(db.PlayerItems.Local.First()).State);
            Assert.Equal(0, await db.PlayerItems.CountAsync(item => item.PlayerId == playerId));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var db = new GameDbContext(options))
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var player = await db.Players.SingleAsync(player => player.Id == playerId);
            var service = new InventoryChangeService(db);
            var prepared = await service.DecreaseWithinCurrentTransactionAsync(player,
                new InventoryDecreaseRequest([new(secondId, 1)],
                    InventoryItemChangeReasonType.AdminAdjustment, suffix, Guid.NewGuid(), DateTime.UtcNow));
            Assert.True(prepared.IsSuccess);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verifyDb = new GameDbContext(options);
        var quantities = await verifyDb.PlayerItems.Where(item => item.PlayerId == playerId)
            .ToDictionaryAsync(item => item.ItemTemplateId, item => item.Quantity);
        Assert.Equal(0, quantities[firstId]);
        Assert.Equal(3, quantities[secondId]);
        var logs = await verifyDb.InventoryItemChangeLogs.Where(log => log.PlayerId == playerId && log.Delta < 0)
            .ToArrayAsync();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, log => Assert.Equal(requestId, log.RequestId));
        Assert.Contains(logs, log => log.ItemTemplateId == firstId && log.Delta == -10 && log.QuantityBefore == 10 && log.QuantityAfter == 0);
        Assert.Contains(logs, log => log.ItemTemplateId == secondId && log.Delta == -2 && log.QuantityBefore == 5 && log.QuantityAfter == 3);
    }
}
