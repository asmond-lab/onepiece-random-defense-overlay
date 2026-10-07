using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticScanContinuationTests
{
    [Fact]
    public void SlowDiagnosticReadsRenewBeforeStartBasedFreshnessExpires()
    {
        var queue = new Queue<Action>();
        var continuation = new DiagnosticScanContinuation();
        var read = TimeSpan.FromMilliseconds(1420);
        var nextCompleted = read + TimeSpan.FromMilliseconds(200) + read;
        Assert.True(nextCompleted >= DiagnosticInventoryObservation.FreshnessBudget);

        Assert.True(continuation.TryQueue(Result(), read, true, queue.Enqueue, () => true,
            () => nextCompleted = read + read));
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.True(nextCompleted < DiagnosticInventoryObservation.FreshnessBudget,
            "A completed slow read must start its next serialized read without waiting for a timer tick.");
        Assert.Equal(TimeSpan.FromSeconds(3), DiagnosticInventoryObservation.FreshnessBudget);
    }

    [Theory]
    [InlineData(RecognitionState.Ready)]
    [InlineData(RecognitionState.TransientReadError)]
    public void SlowNativeDiagnosticResultQueuesOnceUntilConsumed(RecognitionState state)
    {
        var queue = new Queue<Action>();
        var continuation = new DiagnosticScanContinuation();
        var calls = 0;
        bool Schedule() => continuation.TryQueue(Result(state), TimeSpan.FromSeconds(1.4), true,
            queue.Enqueue, () => true, () => calls++);
        Assert.True(Schedule());
        Assert.False(Schedule());
        Assert.Single(queue);
        Assert.Equal(0, calls);
        queue.Dequeue()();
        Assert.Equal(1, calls);
        Assert.True(Schedule());
        queue.Dequeue()();
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(249)]
    public void FastReadsAndErrorsKeepTimerCadence(int elapsedMs)
    {
        foreach (var state in new[] { RecognitionState.Ready, RecognitionState.TransientReadError })
        {
            var queue = new Queue<Action>();
            Assert.False(new DiagnosticScanContinuation().TryQueue(Result(state),
                TimeSpan.FromMilliseconds(elapsedMs), true, queue.Enqueue, () => true,
                () => throw new InvalidOperationException("Fast result started a hot loop")));
            Assert.Empty(queue);
        }
    }

    [Fact]
    public void ControlledNativeDisabledLegacyAndNonReadResultsNeverQueue()
    {
        var queue = new Queue<Action>();
        var continuation = new DiagnosticScanContinuation();
        bool Schedule(RecognitionResult result, bool nativeRead = true) =>
            continuation.TryQueue(result, TimeSpan.FromSeconds(1.4), nativeRead,
                queue.Enqueue, () => true, () => throw new InvalidOperationException("Unexpected read"));
        Assert.False(Schedule(Result(), false));
        Assert.False(Schedule(new RecognitionResult { State = RecognitionState.Ready,
            Diagnostics = new() { Source = "WarcraftMemory" } }));
        foreach (var state in Enum.GetValues<RecognitionState>()
                     .Where(state => state is not RecognitionState.Ready and not RecognitionState.TransientReadError))
            Assert.False(Schedule(Result(state)));
        Assert.Empty(queue);
    }

    [Fact]
    public void LifecycleFenceIsRecheckedWhenQueuedCallbackRuns()
    {
        var queue = new Queue<Action>();
        var continuation = new DiagnosticScanContinuation();
        var current = true;
        var calls = 0;
        bool Schedule() => continuation.TryQueue(Result(), TimeSpan.FromSeconds(1.4), true,
            queue.Enqueue, () => current, () => calls++);
        Assert.True(Schedule());
        current = false;
        queue.Dequeue()();
        Assert.Equal(0, calls);
        Assert.False(Schedule());
        Assert.Empty(queue);
        current = true;
        Assert.True(Schedule());
        queue.Dequeue()();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EnqueueFailureReleasesTheSinglePendingSlot()
    {
        var continuation = new DiagnosticScanContinuation();
        Assert.Throws<InvalidOperationException>(() => continuation.TryQueue(Result(),
            TimeSpan.FromSeconds(1.4), true, _ => throw new InvalidOperationException("Queue stopped"),
            () => true, () => { }));
        var queue = new Queue<Action>();
        Assert.True(continuation.TryQueue(Result(), TimeSpan.FromSeconds(1.4), true,
            queue.Enqueue, () => true, () => { }));
        Assert.Single(queue);
    }

    private static RecognitionResult Result(RecognitionState state = RecognitionState.Ready) => new()
    {
        State = state,
        Diagnostics = new() { Source = DiagnosticInventoryObservation.SourceName }
    };
}
