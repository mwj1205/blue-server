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
        var connectionString = Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!;
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;
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
        var insufficientRequestId = Guid.NewGuid();
        long playerId;

        await using (var arrangeDb = new GameDbContext(options))
        {
            var player = Player.Create(
                $"currency-change-{Guid.NewGuid():N}",
                "integration-test");
            arrangeDb.Players.Add(player);
            await arrangeDb.SaveChangesAsync();
            playerId = player.Id;
        }

        await using (var noTransactionDb = new GameDbContext(options))
        {
            var player = await noTransactionDb.Players.SingleAsync(
                player => player.Id == playerId);
            var service = new CurrencyChangeService(noTransactionDb);

            Assert.Throws<InvalidOperationException>(() =>
                service.ChangeWithinCurrentTransaction(
                    player,
                    CreateRequest(
                        CurrencyType.Gold,
                        1,
                        CurrencyChangeReasonType.AdminAdjustment,
                        "integration:no-transaction",
                        Guid.NewGuid(),
                        changedAt)));
        }

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
            var insufficientResult = service.ChangeWithinCurrentTransaction(
                player,
                CreateRequest(
                    CurrencyType.Gold,
                    -(Player.InitialGold + 201),
                    CurrencyChangeReasonType.ShopPurchase,
                    "integration:insufficient-gold",
                    insufficientRequestId,
                    changedAt.AddSeconds(2)));

            Assert.Equal(CurrencyChangeStatus.Changed, goldResult.Status);
            Assert.Equal(Player.InitialGold + 200, goldResult.CurrentBalance);
            Assert.NotNull(goldResult.Change);
            Assert.Equal(CurrencyChangeStatus.Changed, gemResult.Status);
            Assert.Equal(Player.InitialGem - 100, gemResult.CurrentBalance);
            Assert.NotNull(gemResult.Change);
            Assert.Equal(
                CurrencyChangeStatus.InsufficientBalance,
                insufficientResult.Status);
            Assert.Equal(
                Player.InitialGold + 200,
                insufficientResult.CurrentBalance);
            Assert.Null(insufficientResult.Change);

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
            Assert.DoesNotContain(
                changes,
                change => change.RequestId == insufficientRequestId);
        }
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
