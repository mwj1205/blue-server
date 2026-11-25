using blueServer.Domain.Entities;
using blueServer.Domain.Items;
using blueServer.Infrastructure;
using blueServer.Infrastructure.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.IntegrationTests.Services;

public sealed class InventoryListQueryServiceIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task GetAsync_ReturnsWholeInventoryAndReadsCurrentQuantitiesWithoutTracking()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var options = Options();
        long playerId;
        long otherPlayerId;
        int inactiveId;
        await using (var db = new GameDbContext(options))
        {
            var player = Player.Create($"inventory-query-{Guid.NewGuid():N}");
            var other = Player.Create($"inventory-query-other-{Guid.NewGuid():N}");
            inactiveId = Random.Shared.Next(100_000_000, 1_000_000_000);
            while (await db.ItemTemplates.AnyAsync(item => item.Id == inactiveId, ct)) inactiveId++;
            var code = $"query_{Guid.NewGuid():N}";
            var inactive = ItemTemplate.Create(inactiveId, code, $"item.{code}.name", $"item.{code}.description", ItemType.Material);
            inactive.Deactivate();
            db.AddRange(player, other, inactive);
            await db.SaveChangesAsync(ct);
            playerId = player.Id;
            otherPlayerId = other.Id;
            var templates = await db.ItemTemplates.Where(item => item.Id == 1001 || item.Id == 3101 || item.Id == 3102)
                .ToDictionaryAsync(item => item.Id, ct);
            var empty = PlayerItem.Create(player.Id, templates[3102], 5);
            Assert.True(empty.TryDecrease(5));
            db.PlayerItems.AddRange(
                PlayerItem.Create(player.Id, templates[1001], 2),
                PlayerItem.Create(player.Id, templates[3101], 10),
                empty,
                PlayerItem.Create(player.Id, inactive, 4),
                PlayerItem.Create(other.Id, templates[3101], 999));
            await db.SaveChangesAsync(ct);
        }

        await using var read = new GameDbContext(options);
        var service = new InventoryListQueryService(read);
        var first = await service.GetAsync(playerId, ct);
        Assert.True(first.IsSuccess);
        Assert.Equal(new[] { 1001, 3101, inactiveId }, first.Items.Select(item => item.ItemTemplateId));
        Assert.Equal(10, first.Items[1].Quantity);
        Assert.Equal(ItemType.Material, first.Items[0].Type);
        Assert.Equal(ItemType.Consumable, first.Items[1].Type);
        Assert.False(first.Items[2].IsActive);
        Assert.Empty(read.ChangeTracker.Entries());

        // 같은 조회용 DbContext를 재사용해도 이전 객체가 아닌 현재 DB 수량을 읽는다.
        await using (var update = new GameDbContext(options))
        {
            var item = await update.PlayerItems.SingleAsync(item => item.PlayerId == playerId && item.ItemTemplateId == 3101, ct);
            Assert.True(item.TryDecrease(7));
            await update.SaveChangesAsync(ct);
        }
        var current = await service.GetAsync(playerId, cancellationToken: ct);
        Assert.Equal(3, current.Items.Single(item => item.ItemTemplateId == 3101).Quantity);
        Assert.DoesNotContain(current.Items, item => item.ItemTemplateId == 3102);
        Assert.Equal(3, current.Items.Count);
        var otherItems = await service.GetAsync(otherPlayerId, cancellationToken: ct);
        Assert.Equal(999, Assert.Single(otherItems.Items).Quantity);
        Assert.Empty(read.ChangeTracker.Entries());
    }

    [PostgreSqlIntegrationFact]
    public async Task GetAsync_DistinguishesEmptyInventoryFromMissingPlayer()
    {
        var options = Options();
        long playerId;
        await using (var db = new GameDbContext(options))
        {
            var player = Player.Create($"inventory-query-empty-{Guid.NewGuid():N}");
            db.Players.Add(player);
            await db.SaveChangesAsync();
            playerId = player.Id;
        }
        await using var read = new GameDbContext(options);
        var service = new InventoryListQueryService(read);
        var empty = await service.GetAsync(playerId);
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Items);
        var missing = await service.GetAsync(long.MaxValue);
        Assert.Equal(InventoryListStatus.PlayerNotFound, missing.Status);
        Assert.Empty(missing.Items);
    }

    [PostgreSqlIntegrationFact]
    public async Task GetAsync_ReturnsExactLimitAndRejectsOverflowWithoutPartialInventory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var db = new GameDbContext(Options());
        // 대량 경계 데이터만 테스트 트랜잭션 안에 만들고 롤백해 기존 데이터에 영향을 주지 않는다.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var player = Player.Create($"inventory-query-limit-{Guid.NewGuid():N}");
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);
        var start = Random.Shared.Next(1_400_000_000, 1_800_000_000);
        var limit = InventoryListQueryService.MaxReturnedItems;
        while (await db.ItemTemplates.AnyAsync(item => item.Id >= start && item.Id <= start + limit, ct))
        {
            start = Random.Shared.Next(1_400_000_000, 1_800_000_000);
        }
        var prefix = $"query_limit_{Guid.NewGuid():N}_";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ItemTemplates" ("Id", "Code", "NameKey", "DescriptionKey", "Type", "MaxQuantity", "IsActive")
            SELECT id, {prefix} || id::text, 'item.query.name', 'item.query.description', 1, 999999, true
            FROM generate_series({start}, {start + limit}) AS series(id);
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PlayerItems" ("PlayerId", "ItemTemplateId", "Quantity")
            SELECT {player.Id}, "Id", 1 FROM "ItemTemplates" WHERE "Id" >= {start} AND "Id" < {start + limit};
            """, ct);
        db.ChangeTracker.Clear();
        var service = new InventoryListQueryService(db);
        var exact = await service.GetAsync(player.Id, ct);
        Assert.True(exact.IsSuccess);
        Assert.Equal(limit, exact.Items.Count);
        Assert.Equal(start, exact.Items[0].ItemTemplateId);
        Assert.Equal(start + limit - 1, exact.Items[^1].ItemTemplateId);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PlayerItems" ("PlayerId", "ItemTemplateId", "Quantity") VALUES ({player.Id}, {start + limit}, 1);
            """, ct);
        var overflow = await service.GetAsync(player.Id, ct);
        Assert.Equal(InventoryListStatus.InventoryTooLarge, overflow.Status);
        Assert.False(overflow.IsSuccess);
        Assert.Empty(overflow.Items);
        Assert.Empty(db.ChangeTracker.Entries());
        await transaction.RollbackAsync(CancellationToken.None);
    }

    private static DbContextOptions<GameDbContext> Options() =>
        new DbContextOptionsBuilder<GameDbContext>().UseNpgsql(Environment.GetEnvironmentVariable(
            PostgreSqlIntegrationFactAttribute.ConnectionStringEnvironmentVariable)!).Options;
}
