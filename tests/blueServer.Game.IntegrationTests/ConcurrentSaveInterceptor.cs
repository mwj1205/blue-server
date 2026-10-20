using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace blueServer.Game.IntegrationTests;

// 각 요청이 조회를 끝내고 첫 저장에 도달할 때까지 기다려 실제 DB 경합을 유도한다.
internal sealed class ConcurrentSaveInterceptor : SaveChangesInterceptor
{
    private readonly TaskCompletionSource _bothSaving = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _saveCount;
    private int _conflictCount;

    public int ConflictCount => Volatile.Read(ref _conflictCount);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var saveNumber = Interlocked.Increment(ref _saveCount);

        if (saveNumber == 2)
        {
            _bothSaving.TrySetResult();
        }

        if (saveNumber <= 2)
        {
            // 다른 요청이 저장 전에 실패한 경우 테스트가 무한 대기하지 않도록 제한한다.
            await _bothSaving.Task.WaitAsync(
                TimeSpan.FromSeconds(20),
                cancellationToken);
        }

        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Exception is DbUpdateException
            {
                InnerException: PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation
                }
            })
        {
            Interlocked.Increment(ref _conflictCount);
        }

        return Task.CompletedTask;
    }

    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _conflictCount);
        return ValueTask.FromResult(result);
    }
}
