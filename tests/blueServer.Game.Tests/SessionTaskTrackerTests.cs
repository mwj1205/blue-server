using System.Net.Sockets;
using blueServer.Game.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace blueServer.Game.Tests;

public sealed class SessionTaskTrackerTests
{
    [Fact]
    public async Task Track_AddsSessionTask_AndRemovesItWhenTaskCompletes()
    {
        var tracker = CreateTracker();
        var session = CreateSession();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        tracker.Track(session, completion.Task);

        Assert.Equal(1, tracker.ActiveSessionCount);

        completion.SetResult();

        await WaitUntilAsync(() => tracker.ActiveSessionCount == 0);
    }

    [Fact]
    public async Task Track_RemovesSessionTask_WhenTaskFaults()
    {
        var tracker = CreateTracker();
        var session = CreateSession();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        tracker.Track(session, completion.Task);

        Assert.Equal(1, tracker.ActiveSessionCount);

        completion.SetException(new InvalidOperationException("session failed"));

        await WaitUntilAsync(() => tracker.ActiveSessionCount == 0);
    }

    private static SessionTaskTracker CreateTracker()
    {
        return new SessionTaskTracker(
            NullLogger<SessionTaskTracker>.Instance);
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token);
        }
    }
}
