using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticRecognitionStreamTests
{
    [Fact]
    public async Task DeliversEveryFrameInOrderOnOneWorker()
    {
        var threadIds = new List<int>();
        var actual = new List<int>();
        await foreach (var value in DiagnosticRecognitionStream.Run<int>((emit, token) =>
        {
            for (var i = 0; i < 20; i++)
            {
                token.ThrowIfCancellationRequested();
                threadIds.Add(Environment.CurrentManagedThreadId);
                emit(i);
            }
        }, CancellationToken.None)) actual.Add(value);
        Assert.Equal(Enumerable.Range(0, 20), actual);
        Assert.Single(threadIds.Distinct());
    }

    [Fact]
    public async Task DisposingConsumerCancelsBackpressureAndJoinsWorker()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var stream = DiagnosticRecognitionStream.Run<int>((emit, token) =>
        {
            try
            {
                emit(1); Interlocked.Increment(ref writes);
                emit(2); Interlocked.Increment(ref writes);
                blocked.SetResult();
                emit(3); Interlocked.Increment(ref writes);
            }
            finally { stopped.SetResult(); }
        }, CancellationToken.None);
        var iterator = stream.GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, Volatile.Read(ref writes));
        await iterator.DisposeAsync();
        Assert.True(stopped.Task.IsCompletedSuccessfully);
        Assert.Equal(2, Volatile.Read(ref writes));
    }

    [Fact]
    public async Task ProducerFailureFollowsAlreadyProducedFrames()
    {
        var actual = new List<int>();
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var value in DiagnosticRecognitionStream.Run<int>((emit, _) =>
            {
                emit(1);
                throw new InvalidDataException("native failure");
            }, CancellationToken.None)) actual.Add(value);
        });
        Assert.Equal(new[] { 1 }, actual);
        Assert.Equal("native failure", error.Message);
    }

    [Fact]
    public void CheckpointsThrottleFromCompletedFreshReadWithoutCatchUpBursts()
    {
        var elapsed = TimeSpan.Zero;
        var reads = 0;
        var checkpoint = new DiagnosticDiscoveryCheckpoint(() =>
        {
            reads++;
            elapsed += TimeSpan.FromMilliseconds(40);
        }, () => elapsed);
        checkpoint.Poll(); Assert.Equal(0, reads);
        elapsed = TimeSpan.FromMilliseconds(99); checkpoint.Poll(); Assert.Equal(0, reads);
        elapsed = TimeSpan.FromMilliseconds(100); checkpoint.Poll(); Assert.Equal(1, reads);
        elapsed = TimeSpan.FromMilliseconds(239); checkpoint.Poll(); Assert.Equal(1, reads);
        elapsed = TimeSpan.FromSeconds(8); checkpoint.Poll(); checkpoint.Poll(); Assert.Equal(2, reads);
    }
}
