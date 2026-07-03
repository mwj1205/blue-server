using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public sealed class SessionTaskTracker
{
    private readonly ConcurrentDictionary<Guid, Task> _sessionTasks = new();
    private readonly ILogger<SessionTaskTracker> _logger;

    public SessionTaskTracker(ILogger<SessionTaskTracker> logger)
    {
        _logger = logger;
    }

    public int ActiveSessionCount => _sessionTasks.Count;

    public IReadOnlyCollection<Task> GetActiveSessionTasks()
    {
        return _sessionTasks.Values.ToArray();
    }

    public async Task<bool> WaitForAllAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Timeout must be greater than zero.");
        }

        var tasks = GetActiveSessionTasks();

        if (tasks.Count == 0)
        {
            return true;
        }

        var waitTasks = tasks
            .Select(WaitForCompletionAsync)
            .ToArray();

        try
        {
            await Task
                .WhenAll(waitTasks)
                .WaitAsync(timeout, cancellationToken);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Track(Session session, Task sessionTask)
    {
        var tracked = _sessionTasks.TryAdd(session.SessionId, sessionTask);

        if (!tracked)
        {
            _logger.LogWarning(
                "Session task is already tracked. SessionId={SessionId}, PlayerId={PlayerId}",
                session.SessionId,
                session.PlayerId);
        }

        _ = ObserveCompletionAsync(session, sessionTask, tracked);
    }

    private async Task ObserveCompletionAsync(
        Session session,
        Task sessionTask,
        bool tracked)
    {
        try
        {
            await sessionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Session task canceled. SessionId={SessionId}, PlayerId={PlayerId}",
                session.SessionId,
                session.PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Session task failed. SessionId={SessionId}, PlayerId={PlayerId}",
                session.SessionId,
                session.PlayerId);
        }
        finally
        {
            if (tracked)
            {
                _sessionTasks.TryRemove(session.SessionId, out _);
            }
        }
    }

    private static async Task WaitForCompletionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Shutdown waits for task completion. The observing path logs failures.
        }
    }
}
