using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Mails;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed partial class MailClaimAllServiceIntegrationTests
{
    private static readonly DateTime BatchProcessedAt = new(2026, 11, 9, 1, 0, 0, DateTimeKind.Utc);

    [PostgreSqlIntegrationFact]
    public async Task ClaimAllAsync_AccumulatesUnstoredItemAndReplaysOriginalResults()
    {
        var options = CreateDbContextOptions();
        var fixture = await ArrangeItemBatchAsync(options);
        var request = new MailClaimAllRequest(Guid.NewGuid());
        var counter = new SaveChangesCounterInterceptor();
        var claimOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(counter).Options;

        var result = await ClaimItemBatchAsync(claimOptions, fixture.PlayerId, request);

        Assert.Equal(1, counter.Count);
        AssertItemBatchResult(result, fixture);
        await using (var db = new GameDbContext(options))
        {
            var player = await db.Players.SingleAsync(player => player.Id == fixture.PlayerId);
            Assert.True(player.TrySpendGold(50));
            await db.SaveChangesAsync();
        }

        var replay = await ClaimItemBatchAsync(claimOptions, fixture.PlayerId, request);

        Assert.Equal(1, counter.Count);
        AssertItemBatchResult(replay, fixture);
        await AssertItemBatchPersistedAsync(options, fixture, Player.InitialGold + 80);
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAllAsync_EmptyResultDoesNotClaimMailArrivingAfterRequest()
    {
        var options = CreateDbContextOptions();
        await using var db = new GameDbContext(options);
        var player = Player.Create($"mail-batch-empty-{Guid.NewGuid():N}");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        var request = new MailClaimAllRequest(Guid.NewGuid());

        var empty = await ClaimItemBatchAsync(options, player.Id, request);
        Assert.Equal(MailClaimAllRequestStatus.NothingToClaim, empty.Status);
        Assert.Empty(empty.Entries);

        var mail = Mail.Create(
            player.Id,
            "Later Mail",
            "Not part of the original batch",
            BatchProcessedAt,
            rewards: [CurrencyReward.Create(RewardType.Gold, 25)]);
        db.Mails.Add(mail);
        await db.SaveChangesAsync();

        var replay = await ClaimItemBatchAsync(options, player.Id, request);

        Assert.Equal(MailClaimAllRequestStatus.NothingToClaim, replay.Status);
        Assert.Empty(replay.Entries);
        Assert.Equal(BatchProcessedAt, replay.ProcessedAt);
        Assert.Null(await db.Mails.AsNoTracking().Where(candidate => candidate.Id == mail.Id)
            .Select(candidate => candidate.ClaimedAt).SingleAsync());
        Assert.Equal(1, await db.MailClaimAllRecords.CountAsync(record => record.PlayerId == player.Id));
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAllAsync_InactiveItemRollsBackEarlierMailAndCurrency()
    {
        var options = CreateDbContextOptions();
        var fixture = await ArrangeItemBatchAsync(options, inactiveSecondItem: true);
        var request = new MailClaimAllRequest(Guid.NewGuid());

        var result = await ClaimItemBatchAsync(options, fixture.PlayerId, request);

        Assert.Equal(MailClaimAllRequestStatus.ItemTemplateInactive, result.Status);
        await using var db = new GameDbContext(options);
        var player = await db.Players.SingleAsync(player => player.Id == fixture.PlayerId);
        Assert.Equal(Player.InitialGold, player.Gold);
        Assert.False(await db.PlayerItems.AnyAsync(item => item.PlayerId == fixture.PlayerId));
        Assert.False(await db.InventoryItemChangeLogs.AnyAsync(log => log.PlayerId == fixture.PlayerId));
        Assert.False(await db.CurrencyChangeLogs.AnyAsync(log => log.PlayerId == fixture.PlayerId));
        Assert.False(await db.MailClaimAllRecords.AnyAsync(record => record.PlayerId == fixture.PlayerId));
        Assert.False(await db.MailClaimRecords.AnyAsync(record => record.PlayerId == fixture.PlayerId));
        var mails = await db.Mails.Include(mail => mail.ItemAttachments)
            .Where(mail => mail.PlayerId == fixture.PlayerId).ToArrayAsync();
        Assert.All(mails, mail =>
        {
            Assert.Null(mail.ClaimedAt);
            Assert.Equal(0, Assert.Single(mail.ItemAttachments).ClaimedQuantity);
        });
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAllAsync_ConcurrentSameRequestCommitsOneBatchAndReplaysWinner()
    {
        var options = CreateDbContextOptions();
        var fixture = await ArrangeItemBatchAsync(options);
        var request = new MailClaimAllRequest(Guid.NewGuid());
        var gate = new ConcurrentSaveInterceptor();
        var concurrentOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(gate).Options;

        var results = await Task.WhenAll(
            ClaimItemBatchAsync(concurrentOptions, fixture.PlayerId, request),
            ClaimItemBatchAsync(concurrentOptions, fixture.PlayerId, request));

        Assert.True(gate.ConflictCount > 0);
        Assert.All(results, result => AssertItemBatchResult(result, fixture));
        await AssertItemBatchPersistedAsync(options, fixture, Player.InitialGold + 130);
    }

    private static async Task<MailClaimAllRequestResult> ClaimItemBatchAsync(
        DbContextOptions<GameDbContext> options,
        long playerId,
        MailClaimAllRequest request)
    {
        await using var db = new GameDbContext(options);
        return await CreateService(db).ClaimAllAsync(playerId, request, BatchProcessedAt);
    }

    private static async Task<ItemBatchFixture> ArrangeItemBatchAsync(
        DbContextOptions<GameDbContext> options,
        bool inactiveSecondItem = false)
    {
        await using var db = new GameDbContext(options);
        var player = Player.Create($"mail-batch-{Guid.NewGuid():N}");
        var template = await AddBatchTemplateAsync(db);
        var secondTemplate = inactiveSecondItem ? await AddBatchTemplateAsync(db) : template;
        if (inactiveSecondItem)
        {
            secondTemplate.Deactivate();
        }

        db.Players.Add(player);
        await db.SaveChangesAsync();
        var expiresAt = BatchProcessedAt.AddDays(1);
        var first = Mail.Create(
            player.Id,
            "First item Mail",
            "Oldest first",
            BatchProcessedAt.AddHours(-2),
            expiresAt,
            [CurrencyReward.Create(RewardType.Gold, 100)],
            inventoryItemRewards: [InventoryItemReward.Create(template.Id, 999000)]);
        var second = Mail.Create(
            player.Id,
            "Second item Mail",
            "Keep remaining quantity",
            BatchProcessedAt.AddHours(-1),
            expiresAt,
            [CurrencyReward.Create(RewardType.Gold, 30)],
            inventoryItemRewards: [InventoryItemReward.Create(secondTemplate.Id, 1000)]);
        db.Mails.AddRange(second, first);
        await db.SaveChangesAsync();
        return new ItemBatchFixture(player.Id, template.Id, first.Id, second.Id, expiresAt);
    }

    private static async Task<ItemTemplate> AddBatchTemplateAsync(GameDbContext db)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var id = Random.Shared.Next(1, int.MaxValue);
            if (db.ItemTemplates.Local.Any(template => template.Id == id) ||
                await db.ItemTemplates.AnyAsync(template => template.Id == id))
            {
                continue;
            }

            var code = $"mail_batch_{Guid.NewGuid():N}";
            var template = ItemTemplate.Create(id, code, $"item.{code}.name", $"item.{code}.description", ItemType.Material);
            db.ItemTemplates.Add(template);
            return template;
        }

        throw new InvalidOperationException("Cannot allocate a test ItemTemplate id.");
    }

    private static void AssertItemBatchResult(MailClaimAllRequestResult result, ItemBatchFixture fixture)
    {
        Assert.Equal(MailClaimAllRequestStatus.Processed, result.Status);
        Assert.Equal(BatchProcessedAt, result.ProcessedAt);
        Assert.Equal(1, result.ClaimedMailCount);
        Assert.Equal(130, result.GrantedGold);
        Assert.Equal(Player.InitialGold + 130, result.CurrentGold);
        Assert.False(result.HasMore);
        Assert.Collection(
            result.Entries,
            entry =>
            {
                Assert.Equal(fixture.FirstMailId, entry.MailId);
                Assert.Equal(MailClaimRequestStatus.Claimed, entry.Claim.Status);
                Assert.Equal(new MailClaimItemSnapshot(fixture.TemplateId, 999000, 0, 999000),
                    Assert.Single(entry.Claim.Items));
            },
            entry =>
            {
                Assert.Equal(fixture.SecondMailId, entry.MailId);
                Assert.Equal(MailClaimRequestStatus.PartiallyClaimed, entry.Claim.Status);
                Assert.Equal(new MailClaimItemSnapshot(fixture.TemplateId, 999, 1, 999999),
                    Assert.Single(entry.Claim.Items));
            });
    }

    private static async Task AssertItemBatchPersistedAsync(
        DbContextOptions<GameDbContext> options,
        ItemBatchFixture fixture,
        int currentGold)
    {
        await using var db = new GameDbContext(options);
        var mail = await db.Mails.Include(mail => mail.ItemAttachments)
            .SingleAsync(mail => mail.Id == fixture.SecondMailId);
        Assert.Equal(fixture.ExpiresAt, mail.ExpiresAt);
        Assert.Null(mail.ClaimedAt);
        Assert.Equal(1, Assert.Single(mail.ItemAttachments).RemainingQuantity);
        var player = await db.Players.SingleAsync(player => player.Id == fixture.PlayerId);
        Assert.Equal(currentGold, player.Gold);
        var item = await db.PlayerItems.SingleAsync(item => item.PlayerId == fixture.PlayerId);
        Assert.Equal(999999, item.Quantity);
        var batch = await db.MailClaimAllRecords.Include(record => record.Entries)
            .SingleAsync(record => record.PlayerId == fixture.PlayerId);
        Assert.Equal(2, batch.Entries.Count);
        Assert.Equal(2, await db.MailClaimRecords.CountAsync(record => record.PlayerId == fixture.PlayerId));
        Assert.Equal(2, await db.InventoryItemChangeLogs.CountAsync(log => log.PlayerId == fixture.PlayerId));
    }

    private sealed record ItemBatchFixture(
        long PlayerId,
        int TemplateId,
        long FirstMailId,
        long SecondMailId,
        DateTime ExpiresAt);
}
