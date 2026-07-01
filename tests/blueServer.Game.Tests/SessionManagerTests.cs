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
            provider.GetRequiredService<IServiceScopeFactory>());

        return new Session(
            new TcpClient(),
            dispatcher,
            NullLogger<Session>.Instance);
    }
}
