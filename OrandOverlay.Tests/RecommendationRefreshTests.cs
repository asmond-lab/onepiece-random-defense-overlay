using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationRefreshTests
{
    [Fact]
    public void DefaultRecommendationSettleDelayKeepsUnitDrawRefreshFast()
    {
        Assert.True(
            LatestBackgroundWorkCoordinator.DefaultSettleDelay <=
            TimeSpan.FromMilliseconds(100),
            $"현재 추천 안정 대기: {LatestBackgroundWorkCoordinator.DefaultSettleDelay.TotalMilliseconds}ms");
    }

    [Fact]
    public void FlowStepUsesOnlyRemainingCount()
    {
        var step = new RecipeCraftStep
        {
            UnitId = "rawcode:S20h",
            Name = "조로",
            RequiredCount = 3,
            OwnedCount = 1
        };

        Assert.Equal(2, RecommendationPresentation.FlowRemainingCount(step));
    }

    [Fact]
    public void SupersededRefreshResultCannotReplaceLatestBoard()
    {
        var versions = new LatestRefreshVersion();
        var first = versions.Next();
        var latest = versions.Next();

        Assert.False(versions.IsCurrent(first));
        Assert.True(versions.IsCurrent(latest));
    }

    [Fact]
    public async Task RapidInventoryChangesRunOnlyOneRecommendationAtATime()
    {
        var coordinator = new LatestBackgroundWorkCoordinator(
            _ => Task.CompletedTask);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maximumRunning = 0;
        var priorities =
            new System.Collections.Concurrent.ConcurrentBag<ThreadPriority>();

        string Work(string value, TaskCompletionSource? started = null,
            TaskCompletionSource? release = null)
        {
            var current = Interlocked.Increment(ref running);
            maximumRunning = Math.Max(maximumRunning, current);
            priorities.Add(Thread.CurrentThread.Priority);
            started?.TrySetResult();
            release?.Task.GetAwaiter().GetResult();
            Interlocked.Decrement(ref running);
            return value;
        }

        var first = coordinator.RunAsync(() =>
            Work("first", firstStarted, releaseFirst));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.RunAsync(() => Work("second"));
        var third = coordinator.RunAsync(() => Work("third", thirdStarted));
        releaseFirst.SetResult();

        Assert.Null(await first);
        Assert.Null(await second);
        Assert.Equal("third", await third);
        Assert.Equal(1, maximumRunning);
        Assert.True(thirdStarted.Task.IsCompleted);
        Assert.All(priorities,
            priority => Assert.Equal(ThreadPriority.BelowNormal, priority));
    }

    [Fact]
    public async Task InventoryChangesBeforeSettleRunOnlyLatestRecommendation()
    {
        var releaseSettle = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new LatestBackgroundWorkCoordinator(
            cancellation => releaseSettle.Task.WaitAsync(cancellation));
        var executed = new System.Collections.Concurrent.ConcurrentQueue<string>();

        var first = coordinator.RunAsync(() =>
        {
            executed.Enqueue("first");
            return "first";
        });
        var second = coordinator.RunAsync(() =>
        {
            executed.Enqueue("second");
            return "second";
        });
        var third = coordinator.RunAsync(() =>
        {
            executed.Enqueue("third");
            return "third";
        });
        releaseSettle.SetResult();

        Assert.Null(await first);
        Assert.Null(await second);
        Assert.Equal("third", await third);
        Assert.Equal(["third"], executed);
    }
}
