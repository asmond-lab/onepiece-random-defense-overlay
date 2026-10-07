using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticRecognitionSchedulingTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(13, 100)]
    [InlineData(40, 160)]
    [InlineData(200, 250)]
    public void BasicReadCostBoundsCooldownWithoutCatchUpBursts(int readMs, int cooldownMs)
    {
        var elapsed = TimeSpan.Zero;
        var reads = 0;
        var checkpoint = new DiagnosticDiscoveryCheckpoint(() =>
        {
            reads++;
            elapsed += TimeSpan.FromMilliseconds(readMs);
        }, () => elapsed);
        elapsed = TimeSpan.FromMilliseconds(100);
        checkpoint.Poll();
        Assert.Equal(1, reads);
        var completed = elapsed;
        elapsed = completed + TimeSpan.FromMilliseconds(cooldownMs - 1);
        checkpoint.Poll();
        Assert.Equal(1, reads);
        elapsed += TimeSpan.FromMilliseconds(1);
        checkpoint.Poll();
        Assert.Equal(2, reads);
        elapsed = TimeSpan.FromMinutes(1);
        checkpoint.Poll();
        checkpoint.Poll();
        Assert.Equal(3, reads);
    }

    [Fact]
    public void AdmissionRetriesAtSameTimestampAndThrottlesFromCompletedSample()
    {
        var elapsed = TimeSpan.Zero;
        var capacityAvailable = false;
        var attempts = 0;
        var factoryRuns = 0;
        var checkpoint = new DiagnosticDiscoveryCheckpoint(() =>
        {
            attempts++;
            if (!capacityAvailable) return false;
            factoryRuns++;
            elapsed += TimeSpan.FromMilliseconds(40);
            capacityAvailable = false;
            return true;
        }, () => elapsed);

        elapsed = TimeSpan.FromMilliseconds(99);
        checkpoint.Poll();
        Assert.Equal(0, attempts);
        elapsed = TimeSpan.FromMilliseconds(100);
        checkpoint.Poll();
        Assert.Equal(1, attempts);
        Assert.Equal(0, factoryRuns);

        capacityAvailable = true;
        checkpoint.Poll();
        Assert.Equal(2, attempts);
        Assert.Equal(1, factoryRuns);
        elapsed = TimeSpan.FromMilliseconds(239);
        checkpoint.Poll();
        Assert.Equal(2, attempts);

        elapsed = TimeSpan.FromSeconds(8);
        capacityAvailable = true;
        checkpoint.Poll();
        capacityAvailable = true;
        checkpoint.Poll();
        Assert.Equal(3, attempts);
        Assert.Equal(2, factoryRuns);
    }

    [Fact]
    public async Task OptionalFactoryIsNotInvokedWhileFullAndRequiredFinalRemainsOrdered()
    {
        var firstConsumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var optionalChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryRuns = 0;
        var stream = DiagnosticRecognitionStream.Run<string>((emit, tryEmit, token) =>
        {
            emit("B0");
            firstConsumed.Task.Wait(token);
            emit("B1");
            Assert.False(tryEmit(() =>
            {
                Interlocked.Increment(ref factoryRuns);
                return "B2";
            }));
            optionalChecked.TrySetResult();
            emit("FINAL");
        }, CancellationToken.None);

        var iterator = stream.GetAsyncEnumerator();
        try
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("B0", iterator.Current);
            firstConsumed.TrySetResult();
            await optionalChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Volatile.Read(ref factoryRuns));
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("B1", iterator.Current);
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("FINAL", iterator.Current);
            Assert.False(await iterator.MoveNextAsync());
        }
        finally { await iterator.DisposeAsync(); }
    }

    [Fact]
    public async Task FactoryFailureReleasesAdmissionForRequiredEmission()
    {
        var actual = new List<string>();
        await foreach (var value in DiagnosticRecognitionStream.Run<string>((emit, tryEmit, _) =>
        {
            var error = Assert.Throws<InvalidDataException>(() =>
                tryEmit(() => throw new InvalidDataException("factory failure")));
            Assert.Equal("factory failure", error.Message);
            emit("FINAL");
        }, CancellationToken.None)) actual.Add(value);

        Assert.Equal(new[] { "FINAL" }, actual);
    }

    [Fact]
    public async Task UnhandledFactoryFailureIsDeliveredAsOriginalStreamError()
    {
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in DiagnosticRecognitionStream.Run<string>((_, tryEmit, _) =>
                tryEmit(() => throw new InvalidDataException("factory failure")), CancellationToken.None))
            {
            }
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("factory failure", error.Message);
    }
}
