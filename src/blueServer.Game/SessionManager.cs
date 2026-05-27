using System.Collections.Concurrent;
using System.ComponentModel;

namespace blueServer.Game;

public static class SessionManager
{
    private static readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public static void Add(Session session)
    {
        _sessions.TryAdd(session.SessionId, session);

        Console.WriteLine($"Session Added: {session.SessionId}");
        Console.WriteLine($"Current Sessions: {_sessions.Count}");
    }

    public static void Remove(Session session)
    {
        _sessions.TryRemove(session.SessionId, out _);

        Console.WriteLine($"Session Removed: {session.SessionId}");
        Console.WriteLine($"Current Sessions: {_sessions.Count}");
    }

    public static async Task BroadcastAsync(byte[] data)
    {
        foreach (var session in _sessions.Values)
        {
            await session.SendAsync(data);
        }
    }
}