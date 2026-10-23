using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class CurrencyChangeServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task ChangeWithinCurrentTransaction_ChangesAndPersistsCurrencyHistory()
    {
        var options = CreateOptions();
        var changedAt = new DateTime(
            2026,
            9,
            8,
            1,
            0,
            0,
            DateTimeKind.Utc);
        var goldRequestId = Guid.NewGuid();
        var gemRequestId = Guid.NewGuid();
        var playerId = await CreatePlayerAsync(options);

        await using (var changeDb = new GameDbContext(options))
        await using (var transaction = await changeDb.Database.BeginTransactionAsync())
        {
            var player = await changeDb.Players.SingleAsync(
                player => player.Id == playerId);
            var service = new CurrencyChangeService(changeDb);

            var goldResult = service.ChangeWithinCurrentTransaction(
                player,
                CreateRequest(
                    CurrencyType.Gold,
                    200,
                    CurrencyChangeReasonType.Compensation,
                    "integration:gold-recovery",
                    goldRequestId,
                    changedAt));
            var gemResult = service.ChangeWithinCurrentTransaction(
                player,
                CreateRequest(
                    CurrencyType.Gem,
                    -100,
                    CurrencyChangeReasonType.GachaCost,
                    "integration:gacha",
                    gemRequestId,
                    changedAt.AddSeconds(1)));
            Assert.Equal(CurrencyChangeStatus.Changed, goldResult.Status);
            Assert.Equal(Player.InitialGold + 200, goldResult.CurrentBalance);
            Assert.NotNull(goldResult.Change);
            Assert.Equal(CurrencyChangeStatus.Changed, gemResult.Status);
            Assert.Equal(Player.InitialGem - 100, gemResult.CurrentBalance);
            Assert.NotNull(gemResult.Change);
            await changeDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using (var assertDb = new GameDbContext(options))
        {
            var player = await assertDb.Players
                .AsNoTracking()
                .SingleAsync(player => player.Id == playerId);
            var changes = await assertDb.CurrencyChangeLogs
                .AsNoTracking()
                .Where(change => change.PlayerId == playerId)
                .OrderBy(change => change.CreatedAt)
                .ToArrayAsync();

            Assert.Equal(Player.InitialGold + 200, player.Gold);
            Assert.Equal(Player.InitialGem - 100, player.Gem);
            Assert.Collection(
                changes,
                change =>
                {
                    Assert.Equal(CurrencyType.Gold, change.CurrencyType);
                    Assert.Equal(200, change.Delta);
                    Assert.Equal(Player.InitialGold, change.BalanceBefore);
                    Assert.Equal(Player.InitialGold + 200, change.BalanceAfter);
                    Assert.Equal(goldRequestId, change.RequestId);
                },
                change =>
                {
                    Assert.Equal(CurrencyType.Gem, change.CurrencyType);
                    Assert.Equal(-100, change.Delta);
                    Assert.Equal(Player.InitialGem, change.BalanceBefore);
                    Assert.Equal(Player.InitialGem - 100, change.BalanceAfter);
                    Assert.Equal(gemRequestId, change.RequestId);
                });
        }
    }

    [PostgreSqlIntegrationFact]
    public async Task ChangeWithinCurrentTransaction_LeavesBalanceAndHistoryUnchangedWhenInsufficient()
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);

        await using (var changeDb = new GameDbContext(options))
        await using (var transaction = await changeDb.Database.BeginTransactionAsync())
        {
            var player = await changeDb.Players.SingleAsync(player => player.Id == playerId);
            var result = new CurrencyChangeService(changeDb).ChangeWithinCurrentTransaction(
                player,
                CreateRequest(
                    CurrencyType.Gold,
                    -(Player.InitialGold + 1),
                    CurrencyChangeReasonType.ShopPurchase,
                    "integration:insufficient-gold",
                    Guid.NewGuid(),
                    DateTime.UtcNow));

            Assert.Equal(CurrencyChangeStatus.InsufficientBalance, result.Status);
            Assert.Equal(Player.InitialGold, result.CurrentBalance);
            Assert.Null(result.Change);
            Assert.False(changeDb.ChangeTracker.HasChanges());

            // Rollback에 의존하지 않고 저장과 Commit 후에도 변경이 없는지 검증한다.
            await changeDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await AssertInitialBalanceAndNoHistoryAsync(options, playerId);
    }

    [PostgreSqlIntegrationFact]
    public async Task ChangeWithinCurrentTransaction_RequiresParentTransaction()
    {
        var options = CreateOptions();
        var playerId = await CreatePlayerAsync(options);

        await using (var db = new GameDbContext(options))
        {
            var player = await db.Players.SingleAsync(player => player.Id == playerId);
            var service = new CurrencyChangeService(db);

            Assert.Throws<InvalidOperationException>(() =>
                service.ChangeWithinCurrentTransaction(
                    player,
                    CreateRequest(
                        CurrencyType.Gold,
                        1,
                        CurrencyChangeReasonType.AdminAdjustment,
                        "integration:no-transaction",
                        Guid.NewGuid(),
                        DateTime.UtcNow)));
            Assert.False(db.ChangeTracker.HasChanges());
        }

        await AssertInitialBalanceAndNoHistoryAsync(options, playerId);
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
        var player = Player.Create($"currency-change-{Guid.NewGuid():N}", "integration-test");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    private static async Task AssertInitialBalanceAndNoHistoryAsync(
        DbContextOptions<GameDbContext> options,
        long playerId)
    {
        await using var db = new GameDbContext(options);
        var player = await db.Players.AsNoTracking().SingleAsync(player => player.Id == playerId);
        Assert.Equal(Player.InitialGold, player.Gold);
        Assert.Equal(Player.InitialGem, player.Gem);
        Assert.False(await db.CurrencyChangeLogs.AnyAsync(change => change.PlayerId == playerId));
    }

    private static CurrencyChangeRequest CreateRequest(
        CurrencyType currencyType,
        int delta,
        CurrencyChangeReasonType reasonType,
        string sourceId,
        Guid requestId,
        DateTime changedAt)
    {
        return new CurrencyChangeRequest(
            currencyType,
            delta,
            reasonType,
            sourceId,
            requestId,
            changedAt);
    }
}
