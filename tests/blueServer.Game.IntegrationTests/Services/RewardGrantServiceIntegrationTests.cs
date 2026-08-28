using blueServer.Domain.Entities;
using blueServer.Domain.Rewards;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class RewardGrantServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task GrantAsync_PersistsRewardAndHandlesIdempotentRetry()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var nickname = $"reward-integration-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid();
        var rollbackRequestId = Guid.NewGuid();
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(nickname, "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        var rewards = RewardBundle.Create(
            RewardItem.Create(RewardType.Gold, 150),
            RewardItem.Create(RewardType.Gem, 20));

        // 활성 Transaction 없이 상위 Use Case용 지급 경로를 호출하는 오류 방지 검증
        await using (var noTransactionDb = new GameDbContext(options))
        {
            var service = new RewardGrantService(noTransactionDb);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GrantWithinCurrentTransactionAsync(
                    playerId,
                    rollbackRequestId,
                    "Missing parent transaction test",
                    rewards,
                    CancellationToken.None));
        }

        // 최초 요청의 재화 변경과 지급 이력 저장 검증
        await using (var grantDb = new GameDbContext(options))
        {
            var service = new RewardGrantService(grantDb);
            var result = await service.GrantAsync(
                playerId,
                requestId,
                "Integration test",
                rewards,
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.Granted, result.Status);
            Assert.Equal(Player.InitialGold + 150, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 20, result.CurrentGem);
        }

        // 동일 Request ID와 payload 재시도 시 중복 지급 방지 검증
        await using (var retryDb = new GameDbContext(options))
        {
            var service = new RewardGrantService(retryDb);
            var result = await service.GrantAsync(
                playerId,
                requestId,
                "Integration test",
                rewards,
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.AlreadyGranted, result.Status);
            Assert.Equal(Player.InitialGold + 150, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 20, result.CurrentGem);
        }

        // 동일 Request ID에 다른 payload 사용 시 멱등성 충돌 검증
        await using (var conflictDb = new GameDbContext(options))
        {
            var service = new RewardGrantService(conflictDb);
            var result = await service.GrantAsync(
                playerId,
                requestId,
                "Integration test",
                RewardBundle.Create(
                    RewardItem.Create(RewardType.Gold, 151),
                    RewardItem.Create(RewardType.Gem, 20)),
                CancellationToken.None);

            Assert.Equal(
                RewardGrantStatus.IdempotencyConflict,
                result.Status);
        }

        // 상위 Use Case Transaction Rollback 시 보상 변경도 함께 Rollback되는지 검증
        await using (var rollbackDb = new GameDbContext(options))
        await using (var transaction = await rollbackDb.Database.BeginTransactionAsync())
        {
            var service = new RewardGrantService(rollbackDb);
            var result = await service.GrantWithinCurrentTransactionAsync(
                playerId,
                rollbackRequestId,
                "Parent operation rollback test",
                RewardBundle.Create(
                    RewardItem.Create(RewardType.Gold, 999)),
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.Granted, result.Status);
            await transaction.RollbackAsync();
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

            Assert.Equal(Player.InitialGold + 150, player.Gold);
            Assert.Equal(Player.InitialGem + 20, player.Gem);
            Assert.False(await assertDb.RewardGrantRecords
                .AnyAsync(record =>
                    record.PlayerId == playerId &&
                    record.RequestId == rollbackRequestId));
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
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task GrantBatchWithinCurrentTransactionAsync_GrantsOnlyNewRequestsAndRejectsPayloadConflict()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var existingRequestId = Guid.NewGuid();
        var newRequestId = Guid.NewGuid();
        var rejectedRequestId = Guid.NewGuid();
        var existingRewards = RewardBundle.Create(
            RewardItem.Create(RewardType.Gold, 10));
        var newRewards = RewardBundle.Create(
            RewardItem.Create(RewardType.Gold, 20),
            RewardItem.Create(RewardType.Gem, 5));
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"reward-batch-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;

            var service = new RewardGrantService(arrangeDb);
            var result = await service.GrantAsync(
                playerId,
                existingRequestId,
                "Existing batch request",
                existingRewards,
                CancellationToken.None);

            Assert.Equal(RewardGrantStatus.Granted, result.Status);
        }

        var requests = new[]
        {
            new RewardGrantRequest(
                existingRequestId,
                "Existing batch request",
                existingRewards),
            new RewardGrantRequest(
                newRequestId,
                "New batch request",
                newRewards)
        };

        // 완료 요청은 제외하고 새로운 요청만 같은 Transaction에서 지급
        await using (var batchDb = new GameDbContext(options))
        await using (var transaction = await batchDb.Database.BeginTransactionAsync())
        {
            var service = new RewardGrantService(batchDb);
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

        // 전체 Batch 재시도에서 추가 지급이 없는지 검증
        await using (var retryDb = new GameDbContext(options))
        await using (var transaction = await retryDb.Database.BeginTransactionAsync())
        {
            var service = new RewardGrantService(retryDb);
            var result = await service.GrantBatchWithinCurrentTransactionAsync(
                playerId,
                requests,
                CancellationToken.None);

            Assert.Equal(
                RewardGrantBatchStatus.AlreadyGranted,
                result.Status);
            Assert.Equal(0, result.GrantedRequestCount);
            Assert.Equal(2, result.AlreadyGrantedRequestCount);
            Assert.Equal(Player.InitialGold + 30, result.CurrentGold);
            Assert.Equal(Player.InitialGem + 5, result.CurrentGem);

            await transaction.CommitAsync();
        }

        // 한 요청의 Payload 충돌 시 새 요청까지 전부 적용하지 않는지 검증
        await using (var conflictDb = new GameDbContext(options))
        await using (var transaction = await conflictDb.Database.BeginTransactionAsync())
        {
            var service = new RewardGrantService(conflictDb);
            var result = await service.GrantBatchWithinCurrentTransactionAsync(
                playerId,
                [
                    new RewardGrantRequest(
                        existingRequestId,
                        "Existing batch request",
                        RewardBundle.Create(
                            RewardItem.Create(RewardType.Gold, 11))),
                    new RewardGrantRequest(
                        rejectedRequestId,
                        "Rejected batch request",
                        RewardBundle.Create(
                            RewardItem.Create(RewardType.Gold, 999)))
                ],
                CancellationToken.None);

            Assert.Equal(
                RewardGrantBatchStatus.IdempotencyConflict,
                result.Status);

            await transaction.RollbackAsync();
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

            Assert.Equal(Player.InitialGold + 30, player.Gold);
            Assert.Equal(Player.InitialGem + 5, player.Gem);
            Assert.Contains(existingRequestId, requestIds);
            Assert.Contains(newRequestId, requestIds);
            Assert.DoesNotContain(rejectedRequestId, requestIds);
            Assert.Equal(2, requestIds.Length);
        }
    }
}
