using Microsoft.EntityFrameworkCore.Diagnostics;

namespace blueServer.Game.IntegrationTests;

internal sealed class SaveChangesCounterInterceptor : SaveChangesInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }
}
