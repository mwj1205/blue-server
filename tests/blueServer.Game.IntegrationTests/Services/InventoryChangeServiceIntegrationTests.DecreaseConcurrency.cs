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
    public async Task Decrease_OverlappingTransactions_RejectsStaleWriteAndRollsBackWholeOperation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var fixture = await ArrangeConsumptionAsync(ct);
        var winningId = Guid.NewGuid();
        var losingId = Guid.NewGuid();

        await using (var winner = new GameDbContext(fixture.Options))
        await using (var loser = new GameDbContext(fixture.Options))
        await using (var winnerTransaction = await winner.Database.BeginTransactionAsync(ct))
        await using (var loserTransaction = await loser.Database.BeginTransactionAsync(ct))
        {
            var winnerPlayer = await winner.Players.SingleAsync(player => player.Id == fixture.PlayerId, ct);
            var loserPlayer = await loser.Players.SingleAsync(player => player.Id == fixture.PlayerId, ct);
            // 소비 전 이미 저장한 상위 작업도 최종 실패 시 함께 롤백해야 한다.
            loserPlayer.Gold += 50;
            await loser.SaveChangesAsync(ct);

            var winning = await new InventoryChangeService(winner).DecreaseWithinCurrentTransactionAsync(
                winnerPlayer,
                ConsumptionRequest(winningId, new InventoryDecreaseItem(fixture.FirstId, 7)),
                ct);
            var losing = await new InventoryChangeService(loser).DecreaseWithinCurrentTransactionAsync(
                loserPlayer,
                ConsumptionRequest(losingId, new(fixture.FirstId, 7), new(fixture.SecondId, 2)),
                ct);
            Assert.True(winning.IsSuccess);
            Assert.True(losing.IsSuccess);
            Assert.Equal(3, Assert.Single(winning.Items).QuantityAfter);
            Assert.Equal(3, losing.Items.Single(item => item.ItemTemplateId == fixture.FirstId).QuantityAfter);

            // 둘 다 같은 xmin을 읽은 상태에서 저장 순서를 고정한다. Sleep 기반 경합 테스트가 아니다.
            await winner.SaveChangesAsync(ct);
            await winnerTransaction.CommitAsync(ct);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync(ct));
            await loserTransaction.RollbackAsync(CancellationToken.None);
            // 롤백만으로 추적 객체 값이 복구되지는 않는다. 이 DbContext는 여기서 폐기한다.
        }

        await using var verify = new GameDbContext(fixture.Options);
        var quantities = await verify.PlayerItems.Where(item => item.PlayerId == fixture.PlayerId)
            .ToDictionaryAsync(item => item.ItemTemplateId, item => item.Quantity, ct);
        Assert.Equal(3, quantities[fixture.FirstId]);
        Assert.Equal(5, quantities[fixture.SecondId]);
        Assert.Equal(Player.InitialGold, await verify.Players.Where(player => player.Id == fixture.PlayerId)
            .Select(player => player.Gold).SingleAsync(ct));
        var log = Assert.Single(await verify.InventoryItemChangeLogs
            .Where(log => log.PlayerId == fixture.PlayerId).ToArrayAsync(ct));
        Assert.Equal(winningId, log.RequestId);
        Assert.Equal(-7, log.Delta);

        await using var retry = new GameDbContext(fixture.Options);
        await using var retryTransaction = await retry.Database.BeginTransactionAsync(ct);
        var retryPlayer = await retry.Players.SingleAsync(player => player.Id == fixture.PlayerId, ct);
        var rejected = await new InventoryChangeService(retry).DecreaseWithinCurrentTransactionAsync(
            retryPlayer,
            ConsumptionRequest(losingId, new(fixture.FirstId, 7), new(fixture.SecondId, 2)),
            ct);
        Assert.Equal(InventoryDecreaseStatus.InsufficientQuantity, rejected.Status);
        Assert.Equal(3, rejected.AvailableQuantity);
        Assert.Empty(retry.InventoryItemChangeLogs.Local);
    }

    [PostgreSqlIntegrationFact]
    public async Task Decrease_RejectsInvalidCallAndUnavailableItemsWithoutPreparingChanges()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var fixture = await ArrangeConsumptionAsync(ct);
        await using var db = new GameDbContext(fixture.Options);
        var service = new InventoryChangeService(db);
        var player = await db.Players.SingleAsync(player => player.Id == fixture.PlayerId, ct);
        var request = ConsumptionRequest(Guid.NewGuid(), new InventoryDecreaseItem(fixture.FirstId, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecreaseWithinCurrentTransactionAsync(player, request, ct));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var detachedPlayer = new Player { Id = fixture.PlayerId };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecreaseWithinCurrentTransactionAsync(detachedPlayer, request, ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.DecreaseWithinCurrentTransactionAsync(player, request, cancelled.Token));

        var missingId = await CreateUnusedTemplateIdAsync(db);
        var missing = await service.DecreaseWithinCurrentTransactionAsync(
            player,
            ConsumptionRequest(Guid.NewGuid(), new(fixture.FirstId, 1), new(missingId, 1)),
            ct);
        Assert.Equal(InventoryDecreaseStatus.ItemTemplateNotFound, missing.Status);

        // 정의가 있지만 PlayerItem이 없는 경우에는 보유량 0으로 거절한다.
        var unownedId = await CreateUnusedTemplateIdAsync(db, missingId);
        db.ItemTemplates.Add(CreateTemplate(unownedId, $"unowned_{Guid.NewGuid():N}"));
        await db.SaveChangesAsync(ct);
        var unowned = await service.DecreaseWithinCurrentTransactionAsync(
            player,
            ConsumptionRequest(Guid.NewGuid(), new(fixture.FirstId, 1), new(unownedId, 1)),
            ct);
        Assert.Equal(InventoryDecreaseStatus.InsufficientQuantity, unowned.Status);
        Assert.Equal(0, unowned.AvailableQuantity);

        var inactiveTemplate = await db.ItemTemplates.SingleAsync(template => template.Id == fixture.SecondId, ct);
        inactiveTemplate.Deactivate();
        await db.SaveChangesAsync(ct);
        var inactive = await service.DecreaseWithinCurrentTransactionAsync(
            player,
            ConsumptionRequest(Guid.NewGuid(), new(fixture.FirstId, 1), new(fixture.SecondId, 1)),
            ct);
        Assert.Equal(InventoryDecreaseStatus.ItemTemplateInactive, inactive.Status);

        Assert.Empty(db.InventoryItemChangeLogs.Local);
        Assert.Equal(0, await db.SaveChangesAsync(ct));
        await transaction.RollbackAsync(ct);
        await using var verify = new GameDbContext(fixture.Options);
        Assert.Equal(15, await verify.PlayerItems.Where(item => item.PlayerId == fixture.PlayerId)
            .SumAsync(item => item.Quantity, ct));
        Assert.False(await verify.InventoryItemChangeLogs.AnyAsync(log => log.PlayerId == fixture.PlayerId, ct));
    }

    private static InventoryDecreaseRequest ConsumptionRequest(
        Guid requestId,
        params InventoryDecreaseItem[] items) =>
        new(items, InventoryItemChangeReasonType.AdminAdjustment,
            "inventory-consumption-test", requestId, DateTime.UtcNow);

    private static async Task<ConsumptionFixture> ArrangeConsumptionAsync(CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!)
            .Options;
        await using var db = new GameDbContext(options);
        var suffix = Guid.NewGuid().ToString("N");
        var firstId = await CreateUnusedTemplateIdAsync(db);
        var secondId = await CreateUnusedTemplateIdAsync(db, firstId);
        var player = Player.Create($"consume-boundary-{suffix}");
        var first = CreateTemplate(firstId, $"consume_first_{suffix}");
        var second = CreateTemplate(secondId, $"consume_second_{suffix}");
        db.AddRange(player, first, second);
        await db.SaveChangesAsync(ct);
        db.PlayerItems.AddRange(PlayerItem.Create(player.Id, first, 10), PlayerItem.Create(player.Id, second, 5));
        await db.SaveChangesAsync(ct);
        return new ConsumptionFixture(options, player.Id, firstId, secondId);
    }

    private sealed record ConsumptionFixture(
        DbContextOptions<GameDbContext> Options, long PlayerId, int FirstId, int SecondId);
}
