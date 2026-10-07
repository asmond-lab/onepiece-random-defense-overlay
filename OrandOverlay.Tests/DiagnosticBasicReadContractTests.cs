using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticBasicReadContractTests
{
    private static readonly Lazy<DataCatalog> Catalog = new(() =>
    {
        var catalog = new DataCatalog(); catalog.Load(mapVersion: "2.321"); return catalog;
    });

    [Fact]
    public void BasicOnlyContractRejectsSuccessfulFullCompletion()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticBasicInventoryRead.ForFailure(new() { State = RecognitionState.Ready }));
        foreach (var state in new[] { RecognitionState.Waiting, RecognitionState.TransientReadError,
            RecognitionState.ConfigurationError, RecognitionState.Unsupported, RecognitionState.UnverifiedProfile })
        {
            var failure = new RecognitionResult { State = state };
            var read = DiagnosticBasicInventoryRead.ForFailure(failure);
            Assert.Same(failure, read.Failure);
            Assert.Null(read.Sample);
        }
    }

    [Theory]
    [InlineData("full", "basic")]
    [InlineData("basic", "full")]
    public async Task NativeAdmissionExcludesOtherLaneAndCancellationNeverEnters(string firstLane, string secondLane)
    {
        var service = new WarcraftMemoryRecognitionService(Catalog.Value, Path.GetTempPath());
        using var activeCancellation = new CancellationTokenSource();
        using var waitingCancellation = new CancellationTokenSource();
        var entered = Signal(); var exited = Signal();
        var reads = new List<string>();
        var active = Task.Run(() => service.RunNativeRead(() =>
        {
            reads.Add(firstLane); entered.SetResult();
            try { activeCancellation.Token.WaitHandle.WaitOne(); activeCancellation.Token.ThrowIfCancellationRequested(); }
            finally { exited.SetResult(); }
            return 1;
        }, activeCancellation.Token));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // WaitAsync registers on the exact admission semaphore before returning. Unlike
            // signalling from a second Task.Run, this cannot pass because cancellation won a race.
            var admission = (SemaphoreSlim)typeof(WarcraftMemoryRecognitionService).GetField("_nativeReadAdmission",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service)!;
            async Task OtherLane()
            {
                await admission.WaitAsync(waitingCancellation.Token);
                try { reads.Add(secondLane); }
                finally { admission.Release(); }
            }
            var waiting = OtherLane();
            Assert.False(waiting.IsCompleted);
            waitingCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { firstLane }, reads);
            Assert.False(exited.Task.IsCompleted);
        }
        finally
        {
            activeCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.True(exited.Task.IsCompletedSuccessfully);
        Assert.Equal(3, service.RunNativeRead(() => 3, CancellationToken.None));
    }

    [Fact]
    public void NativeAdmissionReleasesAfterReadFailureAndRejectsPreCancelledWork()
    {
        var service = new WarcraftMemoryRecognitionService(Catalog.Value, Path.GetTempPath());
        Assert.Throws<IOException>(() => service.RunNativeRead<int>(() => throw new IOException("fixture"), CancellationToken.None));
        Assert.Equal(4, service.RunNativeRead(() => 4, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var reads = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => service.RunNativeRead(() => ++reads, cancellation.Token));
        Assert.Equal(0, reads);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
