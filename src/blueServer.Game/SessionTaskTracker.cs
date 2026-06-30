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
}
