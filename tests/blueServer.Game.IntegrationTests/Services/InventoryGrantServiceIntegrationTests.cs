using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Items;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class InventoryGrantServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task GrantWithinCurrentTransactionAsync_CommitsInventoryLogAndOverflowMailTogether()
    {
        var options = CreateOptions();
        var suffix = Guid.NewGuid().ToString("N");
        var sourceId = $"inventory-overflow:{suffix}";
        var requestId = Guid.NewGuid();
        var sentAt = DateTime.UtcNow;
        long playerId;
        int itemTemplateId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            itemTemplateId = await CreateUnusedTemplateIdAsync(arrangeDb);
            var player = Player.Create(
                $"inventory-grant-{suffix}",
                "integration-test");
            var itemTemplate = CreateTemplate(
                itemTemplateId,
                $"integration_grant_{suffix}");

            arrangeDb.AddRange(player, itemTemplate);
            await arrangeDb.SaveChangesAsync();

            arrangeDb.PlayerItems.Add(PlayerItem.Create(
                player.Id,
                itemTemplate,
                999_000));
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        InventoryGrantResult result;

        await using (var grantDb = new GameDbContext(options))
        await using (var transaction = await grantDb.Database.BeginTransactionAsync())
        {
            var player = await grantDb.Players.SingleAsync(
                candidate => candidate.Id == playerId);
            var service = CreateService(grantDb);

            result = await service.GrantWithinCurrentTransactionAsync(
                player,
                CreateRequest(
                    itemTemplateId,
                    amount: 1_200,
                    sourceId,
                    requestId,
                    sentAt));

            Assert.Equal(InventoryGrantStatus.Granted, result.Status);
            Assert.Equal(
                MailDeliveryStatus.Delivered,
                result.OverflowMailResult?.Status);

            await transaction.CommitAsync();
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var playerItem = await assertDb.PlayerItems
                .AsNoTracking()
                .SingleAsync(item =>
                    item.PlayerId == playerId &&
                    item.ItemTemplateId == itemTemplateId);
            var change = await assertDb.InventoryItemChangeLogs
                .AsNoTracking()
                .SingleAsync(item =>
                    item.PlayerId == playerId &&
                    item.RequestId == requestId &&
                    item.ItemTemplateId == itemTemplateId);
            var mail = await assertDb.Mails
                .AsNoTracking()
                .Include(candidate => candidate.ItemAttachments)
                .SingleAsync(candidate =>
                    candidate.PlayerId == playerId &&
                    candidate.SourceType == MailSourceType.System &&
                    candidate.SourceId == sourceId);

            Assert.Equal(ItemTemplate.InventoryMaxQuantity, playerItem.Quantity);
            Assert.Equal(999, change.Delta);
            Assert.Equal(999_000, change.QuantityBefore);
            Assert.Equal(ItemTemplate.InventoryMaxQuantity, change.QuantityAfter);
            Assert.Equal(mail.SentAt.AddDays(30), mail.ExpiresAt);
            Assert.Collection(
                mail.ItemAttachments,
                attachment =>
                {
                    Assert.Equal(itemTemplateId, attachment.ItemTemplateId);
                    Assert.Equal(201, attachment.Quantity);
                });
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantWithinCurrentTransactionAsync_RollsBackInventoryWhenOverflowMailConflicts()
    {
        var options = CreateOptions();
        var suffix = Guid.NewGuid().ToString("N");
        var sourceId = $"inventory-overflow-conflict:{suffix}";
        var requestId = Guid.NewGuid();
        var sentAt = DateTime.UtcNow;
        long playerId;
        int itemTemplateId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            itemTemplateId = await CreateUnusedTemplateIdAsync(arrangeDb);
            var player = Player.Create(
                $"inventory-grant-conflict-{suffix}",
                "integration-test");
            var itemTemplate = CreateTemplate(
                itemTemplateId,
                $"integration_grant_conflict_{suffix}");

            arrangeDb.AddRange(player, itemTemplate);
            await arrangeDb.SaveChangesAsync();

            arrangeDb.PlayerItems.Add(PlayerItem.Create(
                player.Id,
                itemTemplate,
                999_000));
            arrangeDb.Mails.Add(Mail.Create(
                player.Id,
                "Inventory overflow",
                "Inventory overflow reward.",
                sentAt,
                sentAt.AddDays(30),
                sourceType: MailSourceType.System,
                sourceId: sourceId,
                inventoryItemRewards:
                [InventoryItemReward.Create(itemTemplateId, 999)]));
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        await using (var grantDb = new GameDbContext(options))
        await using (var transaction = await grantDb.Database.BeginTransactionAsync())
        {
            var player = await grantDb.Players.SingleAsync(
                candidate => candidate.Id == playerId);
            var service = CreateService(grantDb);

            var result = await service.GrantWithinCurrentTransactionAsync(
                player,
                CreateRequest(
                    itemTemplateId,
                    amount: 1_200,
                    sourceId,
                    requestId,
                    sentAt));

            Assert.Equal(
                InventoryGrantStatus.OverflowMailRejected,
                result.Status);
            Assert.Equal(
                MailDeliveryStatus.IdempotencyConflict,
                result.OverflowMailResult?.Status);

            await transaction.RollbackAsync();
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var playerItem = await assertDb.PlayerItems
                .AsNoTracking()
                .SingleAsync(item =>
                    item.PlayerId == playerId &&
                    item.ItemTemplateId == itemTemplateId);

            Assert.Equal(999_000, playerItem.Quantity);
            Assert.False(await assertDb.InventoryItemChangeLogs.AnyAsync(change =>
                change.PlayerId == playerId &&
                change.RequestId == requestId));
            Assert.Equal(
                1,
                await assertDb.Mails.CountAsync(mail =>
                    mail.PlayerId == playerId &&
                    mail.SourceType == MailSourceType.System &&
                    mail.SourceId == sourceId));
        }
    }

    private static InventoryGrantService CreateService(GameDbContext db)
    {
        return new InventoryGrantService(
            db,
            new InventoryChangeService(db),
            new MailDeliveryService(db));
    }

    private static InventoryGrantRequest CreateRequest(
        int itemTemplateId,
        int amount,
        string sourceId,
        Guid requestId,
        DateTime sentAt)
    {
        return new InventoryGrantRequest(
            [
                new InventoryIncreaseRequest(
                    itemTemplateId,
                    amount,
                    InventoryItemChangeReasonType.AdminAdjustment,
                    sourceId,
                    requestId,
                    sentAt)
            ],
            new InventoryOverflowMailOptions(
                MailSourceType.System,
                sourceId,
                "Inventory overflow",
                "Inventory overflow reward.",
                sentAt,
                InventoryOverflowMailRetentionPolicy.Standard30Days));
    }

    private static DbContextOptions<GameDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
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
        GameDbContext db)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Random.Shared.Next(1, int.MaxValue);

            if (!await db.ItemTemplates.AnyAsync(
                    template => template.Id == candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "An unused ItemTemplate id could not be generated.");
    }
}
