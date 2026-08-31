using blueServer.Domain.Entities;
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
            [RewardItem.Create(RewardType.Gold, gold)]);
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
