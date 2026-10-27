using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using blueServer.Infrastructure.Mails;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class MailClaimSaveBoundaryIntegrationTests
{
    private static readonly DateTime ClaimedAt = new(2026, 10, 26, 1, 0, 0, DateTimeKind.Utc);

    [PostgreSqlIntegrationFact]
    public Task ClaimAsync_SavesMailWhenRewardWasAlreadyGranted() =>
        VerifyExistingGrantsAsync(claimAll: false, mailCount: 1, existingGrantCount: 1);

    [PostgreSqlIntegrationFact]
    public Task ClaimAllAsync_SavesMailsWhenAllRewardsWereAlreadyGranted() =>
        VerifyExistingGrantsAsync(claimAll: true, mailCount: 2, existingGrantCount: 2);

    [PostgreSqlIntegrationFact]
    public Task ClaimAllAsync_GrantsOnlyNewRewardsInMixedBatch() =>
        VerifyExistingGrantsAsync(claimAll: true, mailCount: 2, existingGrantCount: 1);

    [PostgreSqlIntegrationFact]
    public Task ClaimAsync_RollsBackWhenFailureOccursAfterSave() =>
        VerifyRollbackAsync(claimAll: false, mailCount: 1);

    [PostgreSqlIntegrationFact]
    public Task ClaimAllAsync_RollsBackWhenFailureOccursAfterSave() =>
        VerifyRollbackAsync(claimAll: true, mailCount: 2);

    private static async Task VerifyExistingGrantsAsync(
        bool claimAll,
        int mailCount,
        int existingGrantCount)
    {
        var options = CreateOptions();
        var (playerId, mailIds) = await ArrangeMailsAsync(options, mailCount);

        // 실제 수령 경로로 지급 이력을 만든 후 Mail 상태만 되돌려 복구 상황 구성.
        // 테스트가 생성한 Mail만 수정하며 기존 유저 데이터에는 접근하지 않는다.
        await using (var seedDb = new GameDbContext(options))
        {
            for (var i = 0; i < existingGrantCount; i++)
            {
                var result = await CreateSingleService(seedDb).ClaimAsync(
                    playerId, mailIds[i], ClaimedAt.AddMinutes(-1));
                Assert.Equal(MailClaimStatus.Claimed, result.Status);
            }

            var mails = await seedDb.Mails.Where(mail => mail.PlayerId == playerId).ToArrayAsync();
            foreach (var mail in mails)
            {
                mail.ClaimedAt = null;
                mail.ReadAt = null;
            }
            await seedDb.SaveChangesAsync();
        }

        var counter = new SaveChangesCounterInterceptor();
        var claimOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(counter)
            .Options;
        await using (var claimDb = new GameDbContext(claimOptions))
        {
            await ClaimAsync(claimDb, playerId, mailIds, claimAll);
            Assert.Equal(1, counter.Count);
        }

        // 별도 Context에서 commit 후 실제 저장 상태 확인
        await AssertPersistedStateAsync(options, playerId, mailCount, claimed: true);
    }

    private static async Task VerifyRollbackAsync(bool claimAll, int mailCount)
    {
        var options = CreateOptions();
        var (playerId, mailIds) = await ArrangeMailsAsync(options, mailCount);
        var failure = new FailAfterSaveInterceptor();
        var claimOptions = new DbContextOptionsBuilder<GameDbContext>(options)
            .AddInterceptors(failure)
            .Options;

        await using (var claimDb = new GameDbContext(claimOptions))
        {
            await Assert.ThrowsAsync<InjectedSaveFailureException>(() =>
                ClaimAsync(claimDb, playerId, mailIds, claimAll));

            Assert.True(failure.WasTriggered);
            Assert.Empty(claimDb.ChangeTracker.Entries());
            Assert.Null(claimDb.Database.CurrentTransaction);
        }

        // SQL 실행 후에도 상위 rollback으로 Mail·잔액·모든 이력이 복원되는지 확인
        await AssertPersistedStateAsync(options, playerId, mailCount, claimed: false);

        // 실패한 작업의 재실행이 정상 완료되며 중복 지급하지 않는지 확인
        await using (var retryDb = new GameDbContext(options))
        {
            await ClaimAsync(retryDb, playerId, mailIds, claimAll);
        }
        await AssertPersistedStateAsync(options, playerId, mailCount, claimed: true);
    }

    private static async Task ClaimAsync(
        GameDbContext db, long playerId, long[] mailIds, bool claimAll)
    {
        if (claimAll)
        {
            var result = await new MailClaimAllService(db, CreateRewardService(db))
                .ClaimAllAsync(playerId, ClaimedAt);
            Assert.Equal(MailClaimAllStatus.Claimed, result.Status);
            Assert.Equal(mailIds.Length, result.ClaimedMailCount);
            Assert.Equal(Player.InitialGold + 100 * mailIds.Length, result.CurrentGold);
        }
        else
        {
            var result = await CreateSingleService(db).ClaimAsync(playerId, mailIds[0], ClaimedAt);
            Assert.Equal(MailClaimStatus.Claimed, result.Status);
            Assert.Equal(Player.InitialGold + 100, result.CurrentGold);
        }
    }

    private static async Task AssertPersistedStateAsync(
        DbContextOptions<GameDbContext> options, long playerId, int mailCount, bool claimed)
    {
        await using var db = new GameDbContext(options);
        var player = await db.Players.AsNoTracking().SingleAsync(player => player.Id == playerId);
        var mails = await db.Mails.AsNoTracking().Where(mail => mail.PlayerId == playerId).ToArrayAsync();
        Assert.Equal(mailCount, mails.Length);
        Assert.All(mails, mail =>
        {
            Assert.Equal(claimed ? (DateTime?)ClaimedAt : null, mail.ClaimedAt);
            Assert.Equal(claimed ? (DateTime?)ClaimedAt : null, mail.ReadAt);
        });
        var expectedCount = claimed ? mailCount : 0;
        Assert.Equal(Player.InitialGold + 100 * expectedCount, player.Gold);
        Assert.Equal(Player.InitialGem, player.Gem);
        Assert.Equal(expectedCount, await db.RewardGrantRecords.CountAsync(record => record.PlayerId == playerId));
        Assert.Equal(expectedCount, await db.RewardGrantItems.CountAsync(item => item.RewardGrantRecord!.PlayerId == playerId));
        Assert.Equal(expectedCount, await db.CurrencyChangeLogs.CountAsync(change => change.PlayerId == playerId));
    }

    private static async Task<(long PlayerId, long[] MailIds)> ArrangeMailsAsync(
        DbContextOptions<GameDbContext> options, int mailCount)
    {
        await using var db = new GameDbContext(options);
        var player = Player.Create($"mail-save-boundary-{Guid.NewGuid():N}", "integration-test");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        var mails = Enumerable.Range(0, mailCount).Select(index => Mail.Create(
            player.Id,
            $"Save boundary {index}",
            "Verify claim persistence and rollback.",
            ClaimedAt.AddHours(-1).AddMinutes(index),
            ClaimedAt.AddDays(1),
            [CurrencyReward.Create(RewardType.Gold, 100)])).ToArray();
        db.Mails.AddRange(mails);
        await db.SaveChangesAsync();
        return (player.Id, mails.Select(mail => mail.Id).ToArray());
    }

    private static DbContextOptions<GameDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable(
                PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!)
            .Options;

    private static RewardGrantService CreateRewardService(GameDbContext db) =>
        new(db, new CurrencyChangeService(db));

    private static MailClaimService CreateSingleService(GameDbContext db) =>
        new(db, CreateRewardService(db));

    private sealed class InjectedSaveFailureException : Exception;

    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool WasTriggered { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            // DB 저장 후 명시적 트랜잭션 commit 전에 실패 주입
            WasTriggered = true;
            throw new InjectedSaveFailureException();
        }
    }
}
