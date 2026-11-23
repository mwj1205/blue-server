using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed partial class CurrencyBoxUseServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public Task Use_ConcurrentSameRequest_RestoresWinningRandomResult() =>
        VerifyConcurrentUseAsync(sameRequestId: true, sameQuantity: true);

    [PostgreSqlIntegrationFact]
    public Task Use_ConcurrentSameIdWithDifferentPayload_RejectsLosingPayload() =>
        VerifyConcurrentUseAsync(sameRequestId: true, sameQuantity: false);

    [PostgreSqlIntegrationFact]
    public Task Use_ConcurrentDifferentRequests_DoesNotOverconsumeOrKeepLosingRewards() =>
        VerifyConcurrentUseAsync(sameRequestId: false, sameQuantity: true);

    private static async Task VerifyConcurrentUseAsync(bool sameRequestId, bool sameQuantity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        var fixture = await ArrangeAsync(3103);
        var firstRequest = new CurrencyBoxUseRequest(3103, 7, Guid.NewGuid());
        var secondRequest = new CurrencyBoxUseRequest(
            3103,
            sameQuantity ? 7 : 6,
            sameRequestId ? firstRequest.RequestId : Guid.NewGuid());
        var gate = new ConcurrentSaveInterceptor();
        var options = new DbContextOptionsBuilder<GameDbContext>(fixture.Options)
            .AddInterceptors(gate)
            .Options;
        var firstRandom = new SequenceRandom(20, 20, 20, 20, 20, 20, 20);
        var secondRandom = new SequenceRandom(200, 200, 200, 200, 200, 200, 200);
        CurrencyBoxUseResult[] results;

        await using (var firstDb = new GameDbContext(options))
        await using (var secondDb = new GameDbContext(options))
        {
            // 두 요청 모두 소비·추첨을 준비한 뒤 저장해 같은 xmin에 대한 충돌을 유도한다.
            var first = Service(firstDb, fixture.Catalog, firstRandom).UseAsync(
                fixture.PlayerId,
                firstRequest,
                DateTime.UtcNow,
                ct);
            var second = Service(secondDb, fixture.Catalog, secondRandom).UseAsync(
                fixture.PlayerId,
                secondRequest,
                DateTime.UtcNow,
                ct);
            results = await Task.WhenAll(first, second);
        }

        Assert.True(gate.ConflictCount > 0);
        Assert.Equal(firstRequest.Quantity, firstRandom.Calls);
        Assert.Equal(secondRequest.Quantity, secondRandom.Calls);
        if (sameRequestId && sameQuantity)
        {
            Assert.All(results, result => Assert.True(result.IsSuccess));
            Assert.Equal(results[0], results[1]);
        }
        else
        {
            Assert.Single(results, result => result.IsSuccess);
            Assert.Equal(
                sameRequestId ? CurrencyBoxUseStatus.IdempotencyConflict : CurrencyBoxUseStatus.ConcurrencyConflict,
                Assert.Single(results, result => !result.IsSuccess).Status);
        }

        await using var verify = new GameDbContext(fixture.Options);
        var record = Assert.Single(await verify.CurrencyBoxUseRecords
            .Where(record => record.PlayerId == fixture.PlayerId).ToArrayAsync(ct));
        var winningFirst = record.GrantedAmount == 20 * firstRequest.Quantity;
        var winnerRequest = winningFirst ? firstRequest : secondRequest;
        Assert.Equal(winnerRequest.RequestId, record.RequestId);
        Assert.Equal(winnerRequest.Quantity, record.UsedQuantity);
        Assert.Equal((winningFirst ? 20 : 200) * winnerRequest.Quantity, record.GrantedAmount);
        Assert.Equal(10 - winnerRequest.Quantity, record.QuantityAfter);
        Assert.Equal(record.QuantityAfter, await verify.PlayerItems
            .Where(item => item.PlayerId == fixture.PlayerId).Select(item => item.Quantity).SingleAsync(ct));
        var player = await verify.Players.SingleAsync(player => player.Id == fixture.PlayerId, ct);
        Assert.Equal(Player.InitialGold + record.GrantedAmount, player.Gold);
        Assert.Equal(Player.InitialGem, player.Gem);
        var cost = Assert.Single(await verify.InventoryItemChangeLogs
            .Where(log => log.PlayerId == fixture.PlayerId).ToArrayAsync(ct));
        Assert.Equal(-winnerRequest.Quantity, cost.Delta);
        Assert.Equal(record.RequestId, cost.RequestId);
        var reward = Assert.Single(await verify.CurrencyChangeLogs
            .Where(log => log.PlayerId == fixture.PlayerId).ToArrayAsync(ct));
        Assert.Equal(record.GrantedAmount, reward.Delta);
        Assert.Equal(record.RewardRequestId, reward.RequestId);
        var grant = Assert.Single(await verify.RewardGrantRecords
            .Where(grant => grant.PlayerId == fixture.PlayerId).ToArrayAsync(ct));
        Assert.Equal(record.RewardRequestId, grant.RequestId);

        // 재요청에서는 새 추첨 없이 확정 결과 또는 최신 보유량에 따른 부족 결과를 반환한다.
        await using var retryDb = new GameDbContext(fixture.Options);
        var retryRandom = new SequenceRandom();
        var retryService = Service(retryDb, fixture.Catalog, retryRandom);
        var replay = await retryService.UseAsync(fixture.PlayerId, winnerRequest, DateTime.UtcNow, ct);
        Assert.Equal(results.First(result => result.IsSuccess), replay);
        if (!sameRequestId)
        {
            var loserRequest = winningFirst ? secondRequest : firstRequest;
            var retry = await retryService.UseAsync(fixture.PlayerId, loserRequest, DateTime.UtcNow, ct);
            Assert.Equal(CurrencyBoxUseStatus.InsufficientQuantity, retry.Status);
        }

        Assert.Equal(0, retryRandom.Calls);
        Assert.Equal(1, await verify.CurrencyBoxUseRecords.CountAsync(record => record.PlayerId == fixture.PlayerId, ct));
    }
}
