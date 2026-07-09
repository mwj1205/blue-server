using System.Net.Sockets;
using blueServer.Game.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace blueServer.Game.Tests;

public sealed class SessionManagerTests
{
    [Fact]
    public void Add_DoesNotShareStateAcrossManagerInstances()
    {
        var firstManager = CreateManager();
        var secondManager = CreateManager();
        using var session = CreateSession();

        firstManager.Add(session);

        Assert.Equal(1, firstManager.Count);
        Assert.Equal(0, secondManager.Count);
    }

    [Fact]
    public void Remove_DeletesSessionFromCurrentManagerOnly()
    {
        var manager = CreateManager();
        using var session = CreateSession();

        manager.Add(session);
        var removed = manager.Remove(session);

        Assert.True(removed);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void Add_ReturnsFalse_WhenSessionAlreadyExists()
    {
        var manager = CreateManager();
        using var session = CreateSession();

        var firstAdded = manager.Add(session);
        var secondAdded = manager.Add(session);

        Assert.True(firstAdded);
        Assert.False(secondAdded);
        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void Remove_ReturnsFalse_WhenSessionWasAlreadyRemoved()
    {
        var manager = CreateManager();
        using var session = CreateSession();

        manager.Add(session);
        var firstRemoved = manager.Remove(session);
        var secondRemoved = manager.Remove(session);

        Assert.True(firstRemoved);
        Assert.False(secondRemoved);
        Assert.Equal(0, manager.Count);
    }

    private static SessionManager CreateManager()
    {
        return new SessionManager(
            NullLogger<SessionManager>.Instance);
    }

    private static Session CreateSession()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var dispatcher = new PacketDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PacketDispatcher>.Instance);

        return new Session(
            new TcpClient(),
            dispatcher,
            NullLogger<Session>.Instance);
    }
}
