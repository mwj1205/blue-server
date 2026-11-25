using blueServer.Infrastructure;
using blueServer.Infrastructure.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace blueServer.Game.Tests.Infrastructure;

public sealed class InventoryListQueryServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetAsync_RejectsInvalidPlayerBeforeQuery(long playerId)
    {
        await using var db = new GameDbContext(new DbContextOptions<GameDbContext>());
        var service = new InventoryListQueryService(db);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.GetAsync(playerId));
    }

    [Fact]
    public async Task GetAsync_PropagatesCancellationBeforeQuery()
    {
        await using var db = new GameDbContext(new DbContextOptions<GameDbContext>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InventoryListQueryService(db)
            .GetAsync(1, cancellationToken: cancelled.Token));
    }
}
