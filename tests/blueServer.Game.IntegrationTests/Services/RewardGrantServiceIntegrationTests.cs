using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class RewardGrantServiceIntegrationTests
{
    private static RewardGrantService CreateService(GameDbContext db)
    {
        return new RewardGrantService(
            db,
            new CurrencyChangeService(db));
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantAsync_PersistsCurrencyAndLinkedGrantHistory()
    {
        var options = CreateOptions();
        var requestId = Guid.NewGuid();
        var playerId = await CreatePlayerAsync(options);

        var rewards = RewardBundle.Create(
            CurrencyReward.Create(RewardType.Gold, 150),
            CurrencyReward.Create(RewardType.Gem, 20));

        // 최초 요청의 재화 변경과 지급 이력 저장 검증
        await using (var grantDb = new GameDbContext(options))
        {
            var service = CreateService(grantDb);
            var result = await service.GrantAsync(
                playerId,
                CreateRequest(
                    requestId,
                    "Integration test",
                    rewards),
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.Granted, result.Status);
            Assert.Equal(Player.InitialGold + 150, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 20, result.CurrentGem);
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);
            var grant = await assertDb.RewardGrantRecords
                .AsNoTracking()
                .Include(record => record.Items)
                .SingleAsync(record =>
                    record.PlayerId == playerId &&
                    record.RequestId == requestId);
            var currencyChanges = await assertDb.CurrencyChangeLogs
                .AsNoTracking()
                .Where(change =>
                    change.PlayerId == playerId &&
                    change.RequestId == requestId)
                .OrderBy(change => change.CurrencyType)
                .ToArrayAsync();

            Assert.Equal(Player.InitialGold + 150, player.Gold);
            Assert.Equal(Player.InitialGem + 20, player.Gem);
            Assert.Equal("Integration test", grant.Reason);
            Assert.Collection(
                grant.Items.OrderBy(item => item.Type),
                item =>
                {
                    Assert.Equal(RewardType.Gold, item.Type);
                    Assert.Equal(150, item.Amount);
                },
                item =>
                {
                    Assert.Equal(RewardType.Gem, item.Type);
                    Assert.Equal(20, item.Amount);
                });
            Assert.Collection(
                currencyChanges,
                change =>
                {
                    Assert.Equal(CurrencyType.Gold, change.CurrencyType);
                    Assert.Equal(150, change.Delta);
                    Assert.Equal(Player.InitialGold, change.BalanceBefore);
                    Assert.Equal(Player.InitialGold + 150, change.BalanceAfter);
                    Assert.Equal(
                        CurrencyChangeReasonType.RewardGrant,
                        change.ReasonType);
                    Assert.Equal(
                        $"integration-test:{requestId:N}",
                        change.SourceId);
                    Assert.Equal(requestId, change.RequestId);
                    Assert.Equal(grant.Id, change.RewardGrantRecordId);
                },
                change =>
                {
                    Assert.Equal(CurrencyType.Gem, change.CurrencyType);
                    Assert.Equal(20, change.Delta);
                    Assert.Equal(Player.InitialGem, change.BalanceBefore);
                    Assert.Equal(Player.InitialGem + 20, change.BalanceAfter);
                    Assert.Equal(
                        CurrencyChangeReasonType.RewardGrant,
                        change.ReasonType);
                    Assert.Equal(
                        $"integration-test:{requestId:N}",
                        change.SourceId);
                    Assert.Equal(requestId, change.RequestId);
                    Assert.Equal(grant.Id, change.RewardGrantRecordId);
                });
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantAsync_ReturnsAlreadyGrantedWithoutDuplicateChangesOnRetry()
    {
        await AssertRepeatedGrantAsync(
            request => request,
            RewardGrantStatus.AlreadyGranted);
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantAsync_RejectsDifferentSourceWithSameRequestId()
    {
        await AssertRepeatedGrantAsync(
            request => request with { CurrencyChangeSourceId = "different-source" },
            RewardGrantStatus.IdempotencyConflict);
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantAsync_RejectsDifferentRewardsWithSameRequestId()
    {
        await AssertRepeatedGrantAsync(
            request => request with
            {
                Rewards = RewardBundle.Create(
                    CurrencyReward.Create(RewardType.Gold, 151),
                    CurrencyReward.Create(RewardType.Gem, 20))
            },
            RewardGrantStatus.IdempotencyConflict);
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantWithinCurrentTransactionAsync_RequiresParentTransaction()
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);
        await using var db = new GameDbContext(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(db).GrantWithinCurrentTransactionAsync(
                playerId,
                CreateRequest(
                    Guid.NewGuid(),
                    "Missing parent transaction test",
                    RewardBundle.Create(CurrencyReward.Create(RewardType.Gold, 100))),
                CancellationToken.None));

        await AssertPersistedTotalsAsync(options, playerId, 0, 0, 0, 0);
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantWithinCurrentTransactionAsync_RollsBackCurrencyAndHistoryWithParent()
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);

        await using (var db = new GameDbContext(options))
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var result = await CreateService(db).GrantWithinCurrentTransactionAsync(
                playerId,
                CreateRequest(
                    Guid.NewGuid(),
                    "Parent operation rollback test",
                    RewardBundle.Create(CurrencyReward.Create(RewardType.Gold, 999))),
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.Granted, result.Status);
            Assert.Equal(Player.InitialGold + 999, result.CurrentGold);
            Assert.True(await db.RewardGrantRecords.AnyAsync(record =>
                record.PlayerId == playerId));
            Assert.True(await db.CurrencyChangeLogs.AnyAsync(change =>
                change.PlayerId == playerId));

            await transaction.RollbackAsync();
        }

        await AssertPersistedTotalsAsync(options, playerId, 0, 0, 0, 0);
    }

    private static async Task AssertRepeatedGrantAsync(
        Func<RewardGrantRequest, RewardGrantRequest> createRepeatedRequest,
        RewardGrantStatus expectedStatus)
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);
        var request = CreateRequest(
            Guid.NewGuid(),
            "Integration test",
            RewardBundle.Create(
                CurrencyReward.Create(RewardType.Gold, 150),
                CurrencyReward.Create(RewardType.Gem, 20)));

        await using (var grantDb = new GameDbContext(options))
        {
            var initial = await CreateService(grantDb).GrantAsync(
                playerId, request, CancellationToken.None);
            Assert.Equal(RewardGrantStatus.Granted, initial.Status);
        }

        await using (var retryDb = new GameDbContext(options))
        {
            var result = await CreateService(retryDb).GrantAsync(
                playerId, createRepeatedRequest(request), CancellationToken.None);
            Assert.Equal(expectedStatus, result.Status);

            if (expectedStatus == RewardGrantStatus.AlreadyGranted)
            {
                Assert.Equal(Player.InitialGold + 150, result.CurrentGold);
                Assert.Equal(Player.InitialGem + 20, result.CurrentGem);
            }
        }

        await AssertPersistedTotalsAsync(options, playerId, 150, 20, 1, 2);
    }

    private static async Task AssertPersistedTotalsAsync(
        DbContextOptions<GameDbContext> options,
        long playerId,
        int grantedGold,
        int grantedGem,
        int grantCount,
        int changeCount)
    {
        await using var db = new GameDbContext(options);
        var player = await db.Players.AsNoTracking().SingleAsync(player => player.Id == playerId);
        Assert.Equal(Player.InitialGold + grantedGold, player.Gold);
        Assert.Equal(Player.InitialGem + grantedGem, player.Gem);
        Assert.Equal(grantCount, await db.RewardGrantRecords.CountAsync(record =>
            record.PlayerId == playerId));
        Assert.Equal(changeCount, await db.CurrencyChangeLogs.CountAsync(change =>
            change.PlayerId == playerId));
    }

    private static DbContextOptions<GameDbContext> CreateOptions()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        return new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }

    private static async Task<long> CreatePlayerAsync(DbContextOptions<GameDbContext> options)
    {
        await using var db = new GameDbContext(options);
        var player = Player.Create($"reward-integration-{Guid.NewGuid():N}", "integration-test");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantBatchWithinCurrentTransactionAsync_GrantsOnlyNewRequests()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var existingRequestId = Guid.NewGuid();
        var newRequestId = Guid.NewGuid();
        var existingRewards = RewardBundle.Create(
            CurrencyReward.Create(RewardType.Gold, 10));
        var newRewards = RewardBundle.Create(
            CurrencyReward.Create(RewardType.Gold, 20),
            CurrencyReward.Create(RewardType.Gem, 5));
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"reward-batch-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;

            var service = CreateService(arrangeDb);
            var result = await service.GrantAsync(
                playerId,
                CreateRequest(
                    existingRequestId,
                    "Existing batch request",
                    existingRewards),
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.Granted, result.Status);
        }

        var requests = new[]
        {
            CreateRequest(
                existingRequestId,
                "Existing batch request",
                existingRewards),
            CreateRequest(
                newRequestId,
                "New batch request",
                newRewards)
        };

        // 완료 요청은 제외하고 새로운 요청만 같은 Transaction에서 지급
        await using (var batchDb = new GameDbContext(options))
        await using (var transaction = await batchDb.Database.BeginTransactionAsync())
        {
            var service = CreateService(batchDb);
            var result = await service.GrantBatchWithinCurrentTransactionAsync(
                playerId,
                requests,
                CancellationToken.None);

            Assert.Equal(RewardGrantBatchStatus.Granted, result.Status);
            Assert.Equal(1, result.GrantedRequestCount);
            Assert.Equal(1, result.AlreadyGrantedRequestCount);
            Assert.Equal(Player.InitialGold + 30, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 5, result.CurrentGem);

            await transaction.CommitAsync();
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);
            var requestIds = await assertDb.RewardGrantRecords
                .AsNoTracking()
                .Where(record => record.PlayerId == playerId)
                .Select(record => record.RequestId)
                .ToArrayAsync();
            var currencyChanges = await assertDb.CurrencyChangeLogs
                .AsNoTracking()
                .Where(change => change.PlayerId == playerId)
                .ToArrayAsync();

            Assert.Equal(Player.InitialGold + 30, player.Gold);
            Assert.Equal(Player.InitialGem + 5, player.Gem);
            Assert.Contains(existingRequestId, requestIds);
            Assert.Contains(newRequestId, requestIds);
            Assert.Equal(2, requestIds.Length);
            Assert.Equal(3, currencyChanges.Length);
            Assert.Equal(
                1,
                currencyChanges.Count(change =>
                    change.RequestId == existingRequestId));
            Assert.Equal(
                2,
                currencyChanges.Count(change =>
                    change.RequestId == newRequestId));
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantBatchWithinCurrentTransactionAsync_ReturnsAlreadyGrantedOnBatchRetry()
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);
        var requests = new[]
        {
            CreateRequest(Guid.NewGuid(), "Batch gold", RewardBundle.Create(
                CurrencyReward.Create(RewardType.Gold, 10))),
            CreateRequest(Guid.NewGuid(), "Batch gold and gem", RewardBundle.Create(
                CurrencyReward.Create(RewardType.Gold, 20),
                CurrencyReward.Create(RewardType.Gem, 5)))
        };

        await using (var initialDb = new GameDbContext(options))
        await using (var transaction = await initialDb.Database.BeginTransactionAsync())
        {
            var initial = await CreateService(initialDb).GrantBatchWithinCurrentTransactionAsync(
                playerId, requests, CancellationToken.None);
            Assert.Equal(RewardGrantBatchStatus.Granted, initial.Status);
            await transaction.CommitAsync();
        }

        await using (var retryDb = new GameDbContext(options))
        await using (var transaction = await retryDb.Database.BeginTransactionAsync())
        {
            var result = await CreateService(retryDb).GrantBatchWithinCurrentTransactionAsync(
                playerId, requests, CancellationToken.None);

            Assert.Equal(RewardGrantBatchStatus.AlreadyGranted, result.Status);
            Assert.Equal(0, result.GrantedRequestCount);
            Assert.Equal(2, result.AlreadyGrantedRequestCount);
            Assert.Equal(Player.InitialGold + 30, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 5, result.CurrentGem);
            await transaction.CommitAsync();
        }

        await AssertPersistedTotalsAsync(options, playerId, 30, 5, 2, 3);
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantBatchWithinCurrentTransactionAsync_RejectsEntireBatchWhenPayloadConflicts()
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);
        var existingRequest = CreateRequest(
            Guid.NewGuid(), "Existing batch request",
            RewardBundle.Create(CurrencyReward.Create(RewardType.Gold, 10)));
        var rejectedRequestId = Guid.NewGuid();

        await using (var initialDb = new GameDbContext(options))
        {
            var initial = await CreateService(initialDb).GrantAsync(
                playerId, existingRequest, CancellationToken.None);
            Assert.Equal(RewardGrantStatus.Granted, initial.Status);
        }

        await using (var conflictDb = new GameDbContext(options))
        await using (var transaction = await conflictDb.Database.BeginTransactionAsync())
        {
            var result = await CreateService(conflictDb).GrantBatchWithinCurrentTransactionAsync(
                playerId,
                [
                    CreateRequest(rejectedRequestId, "Rejected batch request",
                        RewardBundle.Create(CurrencyReward.Create(RewardType.Gold, 999))),
                    existingRequest with
                    {
                        Rewards = RewardBundle.Create(CurrencyReward.Create(RewardType.Gold, 11))
                    }
                ],
                CancellationToken.None);

            Assert.Equal(RewardGrantBatchStatus.IdempotencyConflict, result.Status);
            // 상위 Rollback에만 의존하지 않고, 서비스가 변경을 적용하지 않았는지도 확인한다.
            Assert.Equal(Player.InitialGold + 10,
                (await conflictDb.Players.SingleAsync(player => player.Id == playerId)).Gold);
            Assert.False(conflictDb.ChangeTracker.HasChanges());
            await transaction.RollbackAsync();
        }

        await AssertPersistedTotalsAsync(options, playerId, 10, 0, 1, 1);
        await using var assertDb = new GameDbContext(options);
        Assert.False(await assertDb.RewardGrantRecords.AnyAsync(record =>
            record.PlayerId == playerId && record.RequestId == rejectedRequestId));
        Assert.False(await assertDb.CurrencyChangeLogs.AnyAsync(change =>
            change.PlayerId == playerId && change.RequestId == rejectedRequestId));
    }

    private static RewardGrantRequest CreateRequest(
        Guid requestId,
        string reason,
        RewardBundle rewards,
        string? sourceId = null)
    {
        return new RewardGrantRequest(
            requestId,
            reason,
            rewards,
            CurrencyChangeReasonType.RewardGrant,
            sourceId ?? $"integration-test:{requestId:N}");
    }
}
