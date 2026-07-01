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

    [Fact]
    public async Task WaitForAllAsync_ReturnsTrue_WhenTrackedTaskCompletes()
    {
        var tracker = CreateTracker();
        var session = CreateSession();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        tracker.Track(session, completion.Task);
        completion.SetResult();

        var completed = await tracker.WaitForAllAsync(
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(completed);
    }

    [Fact]
    public async Task WaitForAllAsync_ReturnsFalse_WhenTimeoutExpires()
    {
        var tracker = CreateTracker();
        var session = CreateSession();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        tracker.Track(session, completion.Task);

        var completed = await tracker.WaitForAllAsync(
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(completed);

        completion.SetResult();
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
            new SessionManager(NullLogger<SessionManager>.Instance),
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
