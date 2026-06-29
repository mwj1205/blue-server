using System.Collections.Concurrent;

namespace blueServer.Game;

public static class SessionManager
{
    // 동시성이 보장되는 딕셔너리로 실시간 접속 유저 관리
    private static readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    // 새로운 유저 접속 시 목록에 등록
    public static void Add(Session session)
    {
        _sessions.TryAdd(session.SessionId, session);

        Console.WriteLine($"Session Added: {session.SessionId}");
        Console.WriteLine($"Current Sessions: {_sessions.Count}");
    }

    // 유저 접속 종료 시 목록에서 안전하게 제거
    public static void Remove(Session session)
    {
        _sessions.TryRemove(session.SessionId, out _);

        Console.WriteLine($"Session Removed: {session.SessionId}");
        Console.WriteLine($"Current Sessions: {_sessions.Count}");
    }

    public static IEnumerable<Session> GetAll()
    {
        return _sessions.Values;
    }

    // 현재 접속해 있는 모든 유저에게 바이너리 패킷 전송
    // TODO: 더 효율적인 방식으로 broadcast
    public static async Task BroadcastAsync(
        byte[] data,
        CancellationToken cancellationToken = default)
    {
        foreach (var session in _sessions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.SendAsync(data);
        }
    }
}
