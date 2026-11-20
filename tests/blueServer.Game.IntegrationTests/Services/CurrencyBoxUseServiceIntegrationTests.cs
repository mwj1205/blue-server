using blueServer.Domain.Currencies;
using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Currencies;
using blueServer.Infrastructure.GameData;
using blueServer.Infrastructure.Items;
using blueServer.Infrastructure.Rewards;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class CurrencyBoxUseServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task Use_AllFourBoxes_ConsumePayAndReplayOriginalSnapshot()
    {
        await VerifyUseAsync(3101, CurrencyType.Gold, 300);
        await VerifyUseAsync(3102, CurrencyType.Gem, 300);
        await VerifyUseAsync(3103, CurrencyType.Gold, 255);
        await VerifyUseAsync(3104, CurrencyType.Gem, 255);
    }

    private static async Task VerifyUseAsync(int itemId, CurrencyType currency, int amount)
    {
        var fixture = await ArrangeAsync(itemId);
        var random = new SequenceRandom(20, 200, 35);
        var request = new CurrencyBoxUseRequest(itemId, 3, Guid.NewGuid());
        CurrencyBoxUseResult first;
        await using (var db = new GameDbContext(fixture.Options))
        {
            first = await Service(db, fixture.Catalog, random).UseAsync(fixture.PlayerId, request, DateTime.UtcNow);
        }

        Assert.True(first.IsSuccess);
        Assert.NotNull(first.Snapshot);
        Assert.Equal(7, first.Snapshot.QuantityAfter);
        Assert.Equal(amount, first.Snapshot.GrantedAmount);
        Assert.Equal(currency, first.Snapshot.CurrencyType);
        Assert.Equal(Player.InitialGold + (currency == CurrencyType.Gold ? amount : 0), first.Snapshot.CurrentGold);
        Assert.Equal(Player.InitialGem + (currency == CurrencyType.Gem ? amount : 0), first.Snapshot.CurrentGem);
        Assert.Equal(itemId >= 3103 ? 3 : 0, random.Calls);

        // 현재 정의나 잔액이 바뀌어도 완료된 요청은 저장된 결과를 반환한다.
        await using (var db = new GameDbContext(fixture.Options))
        {
            var player = await db.Players.SingleAsync(player => player.Id == fixture.PlayerId);
            player.Gold += 10;
            await db.SaveChangesAsync();
        }

        await using (var db = new GameDbContext(fixture.Options))
        {
            var replayRandom = new SequenceRandom();
            var service = Service(db, new CurrencyBoxCatalog(1, []), replayRandom);
            var replay = await service.UseAsync(fixture.PlayerId, request, DateTime.UtcNow);
            Assert.Equal(first, replay);
            var conflict = await service.UseAsync(
                fixture.PlayerId, new CurrencyBoxUseRequest(itemId, 1, request.RequestId), DateTime.UtcNow);
            Assert.Equal(CurrencyBoxUseStatus.IdempotencyConflict, conflict.Status);
            Assert.Equal(0, replayRandom.Calls);
        }

        await using var verify = new GameDbContext(fixture.Options);
        Assert.Equal(7, await verify.PlayerItems.Where(item => item.PlayerId == fixture.PlayerId)
            .Select(item => item.Quantity).SingleAsync());
        var record = await verify.CurrencyBoxUseRecords.SingleAsync(record => record.PlayerId == fixture.PlayerId);
        var cost = await verify.InventoryItemChangeLogs.SingleAsync(log => log.PlayerId == fixture.PlayerId);
        Assert.Equal(-3, cost.Delta);
        Assert.Equal(InventoryItemChangeReasonType.ItemUseCost, cost.ReasonType);
        Assert.Equal(request.RequestId, cost.RequestId);
        var change = await verify.CurrencyChangeLogs.SingleAsync(log => log.PlayerId == fixture.PlayerId);
        Assert.Equal(CurrencyChangeReasonType.ItemUseReward, change.ReasonType);
        Assert.Equal(record.RewardRequestId, change.RequestId);
        Assert.Equal(amount, change.Delta);
        Assert.Equal(1, await verify.RewardGrantRecords.CountAsync(record => record.PlayerId == fixture.PlayerId));
    }

    [PostgreSqlIntegrationFact]
    public async Task Use_InsufficientQuantityDoesNotDrawAndRewardFailureRollsBackConsumption()
    {
        var fixture = await ArrangeAsync(3103);
        await using (var db = new GameDbContext(fixture.Options))
        {
            var random = new SequenceRandom();
            var result = await Service(db, fixture.Catalog, random).UseAsync(
                fixture.PlayerId, new CurrencyBoxUseRequest(3103, 11, Guid.NewGuid()), DateTime.UtcNow);
            Assert.Equal(CurrencyBoxUseStatus.InsufficientQuantity, result.Status);
            Assert.Equal(0, random.Calls);
        }

        await using (var db = new GameDbContext(fixture.Options))
        {
            var player = await db.Players.SingleAsync(player => player.Id == fixture.PlayerId);
            player.Gold = int.MaxValue;
            await db.SaveChangesAsync();
        }

        await using (var db = new GameDbContext(fixture.Options))
        {
            var service = Service(db, fixture.Catalog, new SequenceRandom(20));
            await Assert.ThrowsAsync<OverflowException>(() => service.UseAsync(
                fixture.PlayerId, new CurrencyBoxUseRequest(3103, 1, Guid.NewGuid()), DateTime.UtcNow));
            Assert.Empty(db.ChangeTracker.Entries());
        }

        await using var verify = new GameDbContext(fixture.Options);
        Assert.Equal(10, await verify.PlayerItems.Where(item => item.PlayerId == fixture.PlayerId)
            .Select(item => item.Quantity).SingleAsync());
        Assert.False(await verify.CurrencyBoxUseRecords.AnyAsync(record => record.PlayerId == fixture.PlayerId));
        Assert.False(await verify.InventoryItemChangeLogs.AnyAsync(log => log.PlayerId == fixture.PlayerId));
        Assert.False(await verify.CurrencyChangeLogs.AnyAsync(log => log.PlayerId == fixture.PlayerId));
        Assert.False(await verify.RewardGrantRecords.AnyAsync(record => record.PlayerId == fixture.PlayerId));
    }

    private static CurrencyBoxUseService Service(GameDbContext db, CurrencyBoxCatalog catalog, ICurrencyBoxRandom random) =>
        new(db, new InventoryChangeService(db), new RewardGrantService(db, new CurrencyChangeService(db)),
            new CurrencyBoxRewardCalculator(random), catalog);

    private static async Task<BoxFixture> ArrangeAsync(int itemId)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable(PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!)
            .Options;
        var catalog = (await new EmbeddedGameDataLoader().LoadAsync()).CurrencyBoxCatalog;
        await using var db = new GameDbContext(options);
        var template = await db.ItemTemplates.SingleAsync(template => template.Id == itemId);
        var player = Player.Create($"box-use-{Guid.NewGuid():N}");
        db.Players.Add(player);
        await db.SaveChangesAsync();
        db.PlayerItems.Add(PlayerItem.Create(player.Id, template, 10));
        await db.SaveChangesAsync();
        return new BoxFixture(options, player.Id, catalog);
    }

    private sealed record BoxFixture(DbContextOptions<GameDbContext> Options, long PlayerId, CurrencyBoxCatalog Catalog);

    private sealed class SequenceRandom(params int[] values) : ICurrencyBoxRandom
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minAmount, int maxAmount) => values[Calls++];
    }
}
