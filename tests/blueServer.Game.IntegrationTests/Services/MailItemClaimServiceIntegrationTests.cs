using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using blueServer.Infrastructure.Items;
using blueServer.Infrastructure.Mails;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailItemClaimServiceIntegrationTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [PostgreSqlIntegrationFact]
    public async Task ClaimAsync_PartialThenCompletePreservesMailAndDoesNotRepeatCurrency()
    {
        var options = CreateOptions();
        var (playerId, mailId, templateId) = await ArrangeAsync(options, 999000, mixedRewards: true);
        var request = new MailClaimRequest(mailId, Guid.NewGuid());
        var counter = new SaveChangesCounterInterceptor();
        var claimOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(counter).Options;

        var partial = await ClaimAsync(claimOptions, playerId, request, Now);

        Assert.Equal(MailClaimRequestStatus.PartiallyClaimed, partial.Status);
        Assert.Equal(1, counter.Count);
        Assert.Equal(100, partial.GrantedGold);
        Assert.Equal(10, partial.GrantedGem);
        Assert.Equal(new MailClaimItemSnapshot(templateId, 999, 1, 999999), Assert.Single(partial.Items));
        await using (var db = new GameDbContext(options))
        {
            var mail = await db.Mails.Include(mail => mail.ItemAttachments).SingleAsync(mail => mail.Id == mailId);
            AssertOriginalMailInformation(mail);
            Assert.Null(mail.ReadAt);
            Assert.Null(mail.ClaimedAt);
            Assert.Equal(1000, Assert.Single(mail.ItemAttachments).Quantity);
            Assert.Equal(999, Assert.Single(mail.ItemAttachments).ClaimedQuantity);

            // 다음 수령을 위한 빈 공간은 테스트 준비에서 직접 만든다.
            var item = await db.PlayerItems.SingleAsync(item => item.PlayerId == playerId);
            Assert.True(item.TryDecrease(10));
            await db.SaveChangesAsync();
        }

        // 만료 후에도 같은 요청의 확정 결과가 우선이며, 빈 공간에 추가 지급하지 않는다.
        var retry = await ClaimAsync(options, playerId, request, Now.AddDays(2));
        Assert.Equal(partial.Status, retry.Status);
        Assert.Equal(partial.ProcessedAt, retry.ProcessedAt);
        Assert.Equal(partial.CurrentGold, retry.CurrentGold);
        Assert.Equal(partial.Items.ToArray(), retry.Items.ToArray());
        var conflict = await ClaimAsync(options, playerId,
            new MailClaimRequest(mailId + 1, request.RequestId), Now);
        Assert.Equal(MailClaimRequestStatus.IdempotencyConflict, conflict.Status);

        var completed = await ClaimAsync(options, playerId,
            new MailClaimRequest(mailId, Guid.NewGuid()), Now.AddMinutes(1));
        Assert.Equal(MailClaimRequestStatus.Claimed, completed.Status);
        Assert.Equal(Now.AddMinutes(1), completed.MailClaimedAt);
        Assert.Equal(0, completed.GrantedGold);
        Assert.Equal(0, completed.GrantedGem);
        Assert.Equal(Player.InitialGold + 100, completed.CurrentGold);
        Assert.Equal(Player.InitialGem + 10, completed.CurrentGem);
        Assert.Equal(new MailClaimItemSnapshot(templateId, 1, 0, 999990), Assert.Single(completed.Items));

        await using var assertDb = new GameDbContext(options);
        var finalMail = await assertDb.Mails.Include(mail => mail.ItemAttachments).SingleAsync(mail => mail.Id == mailId);
        AssertOriginalMailInformation(finalMail);
        Assert.Equal(1000, Assert.Single(finalMail.ItemAttachments).ClaimedQuantity);
        Assert.Equal(2, await assertDb.MailClaimRecords.CountAsync(record => record.PlayerId == playerId));
        Assert.Equal(1, await assertDb.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId));
        Assert.Equal(2, await assertDb.InventoryItemChangeLogs.CountAsync(change => change.PlayerId == playerId));
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAsync_NoCapacityRetryDoesNotUseNewSpace()
    {
        var options = CreateOptions();
        var (playerId, mailId, templateId) = await ArrangeAsync(options, 999999, mixedRewards: false);
        var request = new MailClaimRequest(mailId, Guid.NewGuid());

        var first = await ClaimAsync(options, playerId, request, Now);
        Assert.Equal(MailClaimRequestStatus.NoCapacity, first.Status);
        Assert.Equal(new MailClaimItemSnapshot(templateId, 0, 1000, 999999), Assert.Single(first.Items));

        await using (var db = new GameDbContext(options))
        {
            var mail = await db.Mails.Include(mail => mail.ItemAttachments).SingleAsync(mail => mail.Id == mailId);
            AssertOriginalMailInformation(mail);
            Assert.Null(mail.ReadAt);
            Assert.Null(mail.ClaimedAt);
            Assert.Equal(0, Assert.Single(mail.ItemAttachments).ClaimedQuantity);
            var item = await db.PlayerItems.SingleAsync(item => item.PlayerId == playerId);
            Assert.True(item.TryDecrease(10));
            await db.SaveChangesAsync();
        }

        var retry = await ClaimAsync(options, playerId, request, Now.AddMinutes(1));
        Assert.Equal(first.Status, retry.Status);
        Assert.Equal(first.Items.ToArray(), retry.Items.ToArray());

        var next = await ClaimAsync(options, playerId,
            new MailClaimRequest(mailId, Guid.NewGuid()), Now.AddMinutes(2));
        Assert.Equal(MailClaimRequestStatus.PartiallyClaimed, next.Status);
        Assert.Equal(new MailClaimItemSnapshot(templateId, 10, 990, 999999), Assert.Single(next.Items));

        await using var assertDb = new GameDbContext(options);
        Assert.Equal(2, await assertDb.MailClaimRecords.CountAsync(record => record.PlayerId == playerId));
        Assert.Equal(1, await assertDb.InventoryItemChangeLogs.CountAsync(change => change.PlayerId == playerId));
        Assert.Equal(0, await assertDb.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId));
    }

    [PostgreSqlIntegrationFact]
    public Task ClaimAsync_SameRequestConcurrentlyGrantsExistingItemOnce() =>
        VerifySameRequestCollisionAsync(initialQuantity: 999000, mixedRewards: true);

    [PostgreSqlIntegrationFact]
    public Task ClaimAsync_SameRequestConcurrentlyCreatesItemOnce() =>
        VerifySameRequestCollisionAsync(initialQuantity: 0, mixedRewards: false);

    [PostgreSqlIntegrationFact]
    public async Task ClaimAsync_DifferentRequestsForSameMailReturnConflictWithoutDoubleGrant()
    {
        var options = CreateOptions();
        var (playerId, mailId, _) = await ArrangeAsync(options, 999000, mixedRewards: false);
        var gate = new ConcurrentSaveInterceptor();
        var concurrentOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(gate).Options;
        var requests = new[]
        {
            new MailClaimRequest(mailId, Guid.NewGuid()),
            new MailClaimRequest(mailId, Guid.NewGuid())
        };

        var results = await Task.WhenAll(requests.Select(request =>
            ClaimAsync(concurrentOptions, playerId, request, Now)));

        Assert.Single(results, result => result.Status == MailClaimRequestStatus.PartiallyClaimed);
        Assert.Single(results, result => result.Status == MailClaimRequestStatus.ConcurrencyConflict);
        Assert.True(gate.ConflictCount > 0);
        await AssertGrantCountsAsync(options, playerId, mailId, quantityAfter: 999999,
            claimedQuantity: 999, mixedRewards: false);
    }

    [PostgreSqlIntegrationFact]
    public async Task ClaimAsync_FailureAfterSaveRollsBackAllChangesAndAllowsRetry()
    {
        var options = CreateOptions();
        var (playerId, mailId, _) = await ArrangeAsync(options, 999000, mixedRewards: true);
        var request = new MailClaimRequest(mailId, Guid.NewGuid());
        var failure = new FailAfterClaimSaveInterceptor();
        var failingOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(failure).Options;
        await using (var db = new GameDbContext(failingOptions))
        {
            var service = new MailClaimService(db,
                new RewardGrantService(db, new CurrencyChangeService(db)), new InventoryChangeService(db));
            await Assert.ThrowsAsync<InjectedClaimFailureException>(() =>
                service.ClaimAsync(playerId, request, Now));
            Assert.True(failure.WasTriggered);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Null(db.Database.CurrentTransaction);
        }

        await using (var db = new GameDbContext(options))
        {
            var mail = await db.Mails.Include(mail => mail.ItemAttachments).SingleAsync(mail => mail.Id == mailId);
            AssertOriginalMailInformation(mail);
            Assert.Null(mail.ReadAt);
            Assert.Null(mail.ClaimedAt);
            Assert.Equal(0, Assert.Single(mail.ItemAttachments).ClaimedQuantity);
            Assert.Equal(999000, await db.PlayerItems.Where(item => item.PlayerId == playerId)
                .Select(item => item.Quantity).SingleAsync());
            var player = await db.Players.SingleAsync(player => player.Id == playerId);
            Assert.Equal(Player.InitialGold, player.Gold);
            Assert.Equal(Player.InitialGem, player.Gem);
            Assert.Equal(0, await db.MailClaimRecords.CountAsync(record => record.PlayerId == playerId));
            Assert.Equal(0, await db.MailClaimRecordItems.CountAsync(item => item.MailClaimRecord!.PlayerId == playerId));
            Assert.Equal(0, await db.InventoryItemChangeLogs.CountAsync(change => change.PlayerId == playerId));
            Assert.Equal(0, await db.CurrencyChangeLogs.CountAsync(change => change.PlayerId == playerId));
            Assert.Equal(0, await db.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId));
            Assert.Equal(0, await db.RewardGrantItems.CountAsync(item => item.RewardGrantRecord!.PlayerId == playerId));
        }

        var retry = await ClaimAsync(options, playerId, request, Now);
        Assert.Equal(MailClaimRequestStatus.PartiallyClaimed, retry.Status);
        await AssertGrantCountsAsync(options, playerId, mailId, quantityAfter: 999999,
            claimedQuantity: 999, mixedRewards: true);
    }

    private static async Task VerifySameRequestCollisionAsync(int initialQuantity, bool mixedRewards)
    {
        var options = CreateOptions();
        var (playerId, mailId, _) = await ArrangeAsync(options, initialQuantity, mixedRewards);
        var request = new MailClaimRequest(mailId, Guid.NewGuid());
        var gate = new ConcurrentSaveInterceptor();
        var concurrentOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(gate).Options;

        var results = await Task.WhenAll(
            ClaimAsync(concurrentOptions, playerId, request, Now),
            ClaimAsync(concurrentOptions, playerId, request, Now));

        var expectedStatus = initialQuantity == 0
            ? MailClaimRequestStatus.Claimed : MailClaimRequestStatus.PartiallyClaimed;
        Assert.All(results, result => Assert.Equal(expectedStatus, result.Status));
        Assert.Equal(results[0].Items.ToArray(), results[1].Items.ToArray());
        Assert.Equal(results[0].ProcessedAt, results[1].ProcessedAt);
        Assert.Equal(results[0].GrantedGold, results[1].GrantedGold);
        Assert.Equal(results[0].GrantedGem, results[1].GrantedGem);
        Assert.True(gate.ConflictCount > 0);
        await AssertGrantCountsAsync(options, playerId, mailId,
            quantityAfter: initialQuantity == 0 ? 1000 : 999999,
            claimedQuantity: initialQuantity == 0 ? 1000 : 999, mixedRewards);
    }

    private static async Task AssertGrantCountsAsync(
        DbContextOptions<GameDbContext> options, long playerId, long mailId,
        int quantityAfter, int claimedQuantity, bool mixedRewards)
    {
        await using var db = new GameDbContext(options);
        var mail = await db.Mails.Include(mail => mail.ItemAttachments).SingleAsync(mail => mail.Id == mailId);
        AssertOriginalMailInformation(mail);
        Assert.Equal(claimedQuantity, Assert.Single(mail.ItemAttachments).ClaimedQuantity);
        Assert.Equal(claimedQuantity == 1000 ? (DateTime?)Now : null, mail.ClaimedAt);
        Assert.Equal(quantityAfter, await db.PlayerItems.Where(item => item.PlayerId == playerId)
            .Select(item => item.Quantity).SingleAsync());
        var player = await db.Players.SingleAsync(player => player.Id == playerId);
        Assert.Equal(Player.InitialGold + (mixedRewards ? 100 : 0), player.Gold);
        Assert.Equal(Player.InitialGem + (mixedRewards ? 10 : 0), player.Gem);
        Assert.Equal(1, await db.MailClaimRecords.CountAsync(record => record.PlayerId == playerId));
        Assert.Equal(1, await db.MailClaimRecordItems.CountAsync(item => item.MailClaimRecord!.PlayerId == playerId));
        Assert.Equal(1, await db.InventoryItemChangeLogs.CountAsync(change => change.PlayerId == playerId));
        Assert.Equal(mixedRewards ? 1 : 0, await db.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId));
        Assert.Equal(mixedRewards ? 2 : 0, await db.CurrencyChangeLogs.CountAsync(change => change.PlayerId == playerId));
    }

    private sealed class InjectedClaimFailureException : Exception;

    private sealed class FailAfterClaimSaveInterceptor : SaveChangesInterceptor
    {
        public bool WasTriggered { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            WasTriggered = true;
            throw new InjectedClaimFailureException();
        }
    }

    private static async Task<(long PlayerId, long MailId, int TemplateId)> ArrangeAsync(
        DbContextOptions<GameDbContext> options, int quantity, bool mixedRewards)
    {
        await using var db = new GameDbContext(options);
        var templateId = 0;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Random.Shared.Next(1, int.MaxValue);
            if (!await db.ItemTemplates.AnyAsync(template => template.Id == candidate))
            {
                templateId = candidate;
                break;
            }
        }
        if (templateId == 0) { throw new InvalidOperationException("Cannot allocate test ItemTemplate id."); }
        var code = $"mail_claim_{Guid.NewGuid():N}";
        var template = ItemTemplate.Create(templateId, code, $"item.{code}.name",
            $"item.{code}.description", ItemType.Material);
        var player = Player.Create($"mail-item-claim-{Guid.NewGuid():N}", "integration-test");
        db.AddRange(player, template);
        await db.SaveChangesAsync();
        if (quantity > 0)
        {
            db.PlayerItems.Add(PlayerItem.Create(player.Id, template, quantity));
        }
        var mail = Mail.Create(player.Id, "Original claim mail", "Preserve this body",
            Now.AddHours(-1), Now.AddDays(1),
            mixedRewards ? [CurrencyReward.Create(RewardType.Gold, 100), CurrencyReward.Create(RewardType.Gem, 10)] : null,
            MailSourceType.Event, $"claim-test:{Guid.NewGuid():N}",
            [InventoryItemReward.Create(templateId, 1000)]);
        db.Mails.Add(mail);
        await db.SaveChangesAsync();
        return (player.Id, mail.Id, templateId);
    }

    private static void AssertOriginalMailInformation(Mail mail)
    {
        Assert.Equal("Original claim mail", mail.Title);
        Assert.Equal("Preserve this body", mail.Body);
        Assert.Equal(MailSourceType.Event, mail.SourceType);
        Assert.StartsWith("claim-test:", mail.SourceId);
        Assert.Equal(Now.AddHours(-1), mail.SentAt);
        Assert.Equal(Now.AddDays(1), mail.ExpiresAt);
    }

    private static async Task<MailClaimRequestResult> ClaimAsync(
        DbContextOptions<GameDbContext> options, long playerId, MailClaimRequest request, DateTime now)
    {
        await using var db = new GameDbContext(options);
        return await new MailClaimService(db, new RewardGrantService(db, new CurrencyChangeService(db)),
            new InventoryChangeService(db)).ClaimAsync(playerId, request, now);
    }

    private static DbContextOptions<GameDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!).Options;
}
