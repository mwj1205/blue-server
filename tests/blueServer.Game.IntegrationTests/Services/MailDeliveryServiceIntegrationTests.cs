using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailDeliveryServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task DeliverAsync_DeliversOnceAndRejectsPayloadConflict()
    {
        var options = CreateDbContextOptions();
        var sourceId = $"integration:{Guid.NewGuid():N}";
        var sentAt = new DateTime(
            2026,
            8,
            30,
            12,
            0,
            0,
            7,
            DateTimeKind.Utc).AddTicks(8);
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-delivery-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        var request = CreateRequest(playerId, sourceId, sentAt, 100);
        MailDeliveryResult firstResult;

        await using (var deliveryDb = new GameDbContext(options))
        {
            firstResult = await new MailDeliveryService(deliveryDb)
                .DeliverAsync(request);

            Assert.Equal(MailDeliveryStatus.Delivered, firstResult.Status);
            Assert.True(firstResult.MailId > 0);
        }

        await using (var retryDb = new GameDbContext(options))
        {
            var retryResult = await new MailDeliveryService(retryDb)
                .DeliverAsync(request);

            Assert.Equal(
                MailDeliveryStatus.AlreadyDelivered,
                retryResult.Status);
            Assert.Equal(firstResult.MailId, retryResult.MailId);
        }

        await using (var conflictDb = new GameDbContext(options))
        {
            var conflictResult = await new MailDeliveryService(conflictDb)
                .DeliverAsync(CreateRequest(
                    playerId,
                    sourceId,
                    sentAt,
                    101));

            Assert.Equal(
                MailDeliveryStatus.IdempotencyConflict,
                conflictResult.Status);
            Assert.Equal(firstResult.MailId, conflictResult.MailId);
        }

        await using var assertDb = new GameDbContext(options);
        var mails = await assertDb.Mails
            .AsNoTracking()
            .Include(mail => mail.Attachments)
            .Where(mail =>
                mail.PlayerId == playerId &&
                mail.SourceType == MailSourceType.Event &&
                mail.SourceId == sourceId)
            .ToArrayAsync();

        var mail = Assert.Single(mails);
        Assert.Equal(firstResult.MailId, mail.Id);
        Assert.Equal(
            sentAt.Ticks - sentAt.Ticks % TimeSpan.TicksPerMicrosecond,
            mail.SentAt.Ticks);
        var attachment = Assert.Single(mail.Attachments);
        Assert.Equal(RewardType.Gold, attachment.Type);
        Assert.Equal(100, attachment.Amount);
    }

    [PostgreSqlIntegrationFact]
    public async Task DeliverWithinCurrentTransactionAsync_RollsBackWithParent()
    {
        var options = CreateDbContextOptions();
        var sourceId = $"rollback:{Guid.NewGuid():N}";
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-delivery-rollback-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        await using (var deliveryDb = new GameDbContext(options))
        await using (var transaction =
            await deliveryDb.Database.BeginTransactionAsync())
        {
            var result = await new MailDeliveryService(deliveryDb)
                .DeliverWithinCurrentTransactionAsync(CreateRequest(
                    playerId,
                    sourceId,
                    DateTime.UtcNow,
                    100));

            Assert.Equal(MailDeliveryStatus.Delivered, result.Status);
            await transaction.RollbackAsync();
        }

        await using var assertDb = new GameDbContext(options);
        Assert.False(await assertDb.Mails
            .AsNoTracking()
            .AnyAsync(mail =>
                mail.PlayerId == playerId &&
                mail.SourceId == sourceId));
    }

    [PostgreSqlIntegrationFact]
    public async Task DeliverAsync_PersistsGroupedItemAttachmentsAndRejectsPayloadConflict()
    {
        var options = CreateDbContextOptions();
        var suffix = Guid.NewGuid().ToString("N");
        var sourceId = $"item-delivery:{suffix}";
        var sentAt = DateTime.UtcNow;
        long playerId;
        int firstTemplateId;
        int secondTemplateId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            firstTemplateId = await CreateUnusedTemplateIdAsync(arrangeDb);
            secondTemplateId = await CreateUnusedTemplateIdAsync(
                arrangeDb,
                firstTemplateId);

            var player = Player.Create(
                $"mail-item-delivery-{suffix}",
                "integration-test");
            var firstTemplate = CreateTemplate(
                firstTemplateId,
                $"mail_item_first_{suffix}");
            var secondTemplate = CreateTemplate(
                secondTemplateId,
                $"mail_item_second_{suffix}");

            arrangeDb.AddRange(player, firstTemplate, secondTemplate);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        var request = CreateItemRequest(
            playerId,
            sourceId,
            sentAt,
            [
                InventoryItemReward.Create(firstTemplateId, 600),
                InventoryItemReward.Create(firstTemplateId, 401),
                InventoryItemReward.Create(secondTemplateId, 3)
            ]);
        MailDeliveryResult firstResult;

        await using (var deliveryDb = new GameDbContext(options))
        {
            firstResult = await new MailDeliveryService(deliveryDb)
                .DeliverAsync(request);

            Assert.Equal(MailDeliveryStatus.Delivered, firstResult.Status);
        }

        await using (var retryDb = new GameDbContext(options))
        {
            var retryResult = await new MailDeliveryService(retryDb)
                .DeliverAsync(request);

            Assert.Equal(
                MailDeliveryStatus.AlreadyDelivered,
                retryResult.Status);
            Assert.Equal(firstResult.MailId, retryResult.MailId);
        }

        await using (var conflictDb = new GameDbContext(options))
        {
            var conflictResult = await new MailDeliveryService(conflictDb)
                .DeliverAsync(CreateItemRequest(
                    playerId,
                    sourceId,
                    sentAt,
                    [
                        InventoryItemReward.Create(firstTemplateId, 1_002),
                        InventoryItemReward.Create(secondTemplateId, 3)
                    ]));

            Assert.Equal(
                MailDeliveryStatus.IdempotencyConflict,
                conflictResult.Status);
            Assert.Equal(firstResult.MailId, conflictResult.MailId);
        }

        await using var assertDb = new GameDbContext(options);
        var mails = await assertDb.Mails
            .AsNoTracking()
            .Include(mail => mail.ItemAttachments)
            .Where(mail =>
                mail.PlayerId == playerId &&
                mail.SourceType == MailSourceType.Event &&
                mail.SourceId == sourceId)
            .ToArrayAsync();

        var mail = Assert.Single(mails);
        Assert.Equal(firstResult.MailId, mail.Id);
        Assert.Collection(
            mail.ItemAttachments.OrderBy(attachment => attachment.ItemTemplateId),
            attachment =>
            {
                Assert.Equal(firstTemplateId, attachment.ItemTemplateId);
                Assert.Equal(1_001, attachment.Quantity);
            },
            attachment =>
            {
                Assert.Equal(secondTemplateId, attachment.ItemTemplateId);
                Assert.Equal(3, attachment.Quantity);
            });
    }

    [PostgreSqlIntegrationFact]
    public async Task DeliverAsync_ConcurrentRequestsCreateOneMail()
    {
        var options = CreateDbContextOptions();
        var sourceId = $"concurrent:{Guid.NewGuid():N}";
        var sentAt = DateTime.UtcNow;
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"mail-delivery-concurrent-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        var request = CreateRequest(playerId, sourceId, sentAt, 100);
        var results = await Task.WhenAll(
            DeliverWithNewDbContextAsync(options, request),
            DeliverWithNewDbContextAsync(options, request));

        Assert.Single(
            results,
            result => result.Status == MailDeliveryStatus.Delivered);
        Assert.Single(
            results,
            result => result.Status == MailDeliveryStatus.AlreadyDelivered);
        Assert.Equal(results[0].MailId, results[1].MailId);

        await using var assertDb = new GameDbContext(options);
        Assert.Equal(
            1,
            await assertDb.Mails
                .AsNoTracking()
                .CountAsync(mail =>
                    mail.PlayerId == playerId &&
                    mail.SourceType == MailSourceType.Event &&
                    mail.SourceId == sourceId));
    }

    [PostgreSqlIntegrationFact]
    public async Task DeliverWithinCurrentTransactionAsync_RequiresTransaction()
    {
        var options = CreateDbContextOptions();

        await using var db = new GameDbContext(options);
        var service = new MailDeliveryService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeliverWithinCurrentTransactionAsync(CreateRequest(
                1,
                $"missing-transaction:{Guid.NewGuid():N}",
                DateTime.UtcNow,
                100)));
    }

    private static MailDeliveryRequest CreateRequest(
        long playerId,
        string sourceId,
        DateTime sentAt,
        int gold)
    {
        return new MailDeliveryRequest(
            playerId,
            MailSourceType.Event,
            sourceId,
            "Event reward",
            "Event reward delivery test.",
            sentAt,
            sentAt.AddDays(7),
            [CurrencyReward.Create(RewardType.Gold, gold)]);
    }

    private static MailDeliveryRequest CreateItemRequest(
        long playerId,
        string sourceId,
        DateTime sentAt,
        IReadOnlyList<InventoryItemReward> itemRewards)
    {
        return new MailDeliveryRequest(
            PlayerId: playerId,
            SourceType: MailSourceType.Event,
            SourceId: sourceId,
            Title: "Item event reward",
            Body: "Item event reward delivery test.",
            SentAt: sentAt,
            ExpiresAt: sentAt.AddDays(7),
            Rewards: null,
            InventoryItemRewards: itemRewards);
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

    private static DbContextOptions<GameDbContext> CreateDbContextOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;

        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    private static async Task<MailDeliveryResult> DeliverWithNewDbContextAsync(
        DbContextOptions<GameDbContext> options,
        MailDeliveryRequest request)
    {
        await using var db = new GameDbContext(options);

        return await new MailDeliveryService(db).DeliverAsync(request);
    }
}
