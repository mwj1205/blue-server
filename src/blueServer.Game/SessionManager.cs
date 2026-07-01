using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace blueServer.Game;

public sealed class SessionManager
{
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();
    private readonly ILogger<SessionManager> _logger;

    public SessionManager(ILogger<SessionManager> logger)
    {
        _logger = logger;
    }

    public int Count => _sessions.Count;

    public bool Add(Session session)
    {
        var added = _sessions.TryAdd(session.SessionId, session);

        if (added)
        {
            _logger.LogInformation(
                "Session added. SessionId={SessionId}, ActiveSessionCount={ActiveSessionCount}",
                session.SessionId,
                _sessions.Count);
            return true;
        }

        _logger.LogWarning(
            "Session already exists. SessionId={SessionId}, ActiveSessionCount={ActiveSessionCount}",
            session.SessionId,
            _sessions.Count);

        return false;
    }

    public bool Remove(Session session)
    {
        var removed = _sessions.TryRemove(session.SessionId, out _);

        if (removed)
        {
            _logger.LogInformation(
                "Session removed. SessionId={SessionId}, ActiveSessionCount={ActiveSessionCount}",
                session.SessionId,
                _sessions.Count);
            return true;
        }

        _logger.LogDebug(
            "Session was not registered. SessionId={SessionId}, ActiveSessionCount={ActiveSessionCount}",
            session.SessionId,
            _sessions.Count);

        return false;
    }

    public IReadOnlyCollection<Session> GetAll()
    {
        // 호출자가 순회하는 동안 세션 목록이 바뀔 수 있으므로 현재 시점의 스냅샷 반환
        return _sessions.Values.ToArray();
    }

    public async Task BroadcastAsync(
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        foreach (var session in GetAll())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.SendAsync(data);
        }
    }
}
