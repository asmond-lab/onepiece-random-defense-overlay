using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticCompletionPairTests
{
    private static readonly Lazy<DataCatalog> Data = new(() =>
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.320");
        return catalog;
    });
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Binding = new('A', 64);
    private static readonly string OldWorld = new('B', 64);
    private static readonly string FinalWorld = new('C', 64);
    private static string Fingerprint => Data.Value.OfflineBundle!.Fingerprint;

    private static RecognitionDiagnostics Diagnostics(string source) => new()
    {
        Source = source, ProcessVersion = Warcraft300Diagnostic.Version,
        ExecutableSha256 = Warcraft300Diagnostic.Hash
    };

    private static DiagnosticBasicInventorySample Basic(long revision, string world, DateTimeOffset? time = null,
        string? binding = null, string? source = null) => new(
        DiagnosticBasicInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            binding ?? Binding, revision, 0, (time ?? Now).AddMilliseconds(-10), time ?? Now, TimeSpan.FromMilliseconds(10),
            [new() { UnitId = "rawcode:I10h", Count = 2 }], world, binding ?? Binding),
        Diagnostics(source ?? DiagnosticBasicInventoryObservation.SourceName));

    private static RecognitionResult Full(DiagnosticBasicInventorySample? pair, DateTimeOffset? time = null,
        RecognitionState state = RecognitionState.Ready, bool boundary = false) => new()
    {
        State = state, ConfirmsSessionBoundary = boundary,
        Diagnostics = Diagnostics(DiagnosticInventoryObservation.SourceName), CompletionBasicSample = pair,
        DiagnosticObservation = DiagnosticInventoryObservation.Create(Data.Value,
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, Binding, 1, 0,
            (time ?? Now).AddMilliseconds(-100), time ?? Now, TimeSpan.FromMilliseconds(100),
            [new() { UnitId = "rawcode:I10h", Count = 3 }], ["rawcode:I10h"], 12, FinalWorld, Binding)
    };

    private static DiagnosticInventoryPresentationState WithOldBasic()
    {
        var state = new DiagnosticInventoryPresentationState();
        var sample = Basic(1, OldWorld);
        Assert.True(state.TryAcceptBasic(sample.Observation, sample.Diagnostics, Now, "2.320", Fingerprint, out _));
        return state;
    }

    private static bool Accept(DiagnosticInventoryPresentationState state, RecognitionResult value) =>
        state.TryAcceptFull(value, Now, "2.320", Fingerprint, out _);

    private static IDiagnosticInventoryReference? Current(DiagnosticInventoryPresentationState state) =>
        state.Current(Now, "2.320", Fingerprint);

    [Fact]
    public void CompletedPairReplacesEarlierWorldAtomicallyWithoutAnExtraBasicFrame()
    {
        var state = WithOldBasic();
        Assert.False(Accept(state, Full(null)));
        Assert.Equal(OldWorld, Current(state)!.WorldStampFingerprint);

        state = WithOldBasic();
        var result = Full(Basic(2, FinalWorld));
        Assert.True(Accept(state, result));
        Assert.Same(result.DiagnosticObservation, Current(state));
        Assert.True(Current(state)!.GrowthAttributionAvailable);
        Assert.Equal(3, Current(state)!.Counts["rawcode:I10h"]);
        Assert.False(Current(state)!.GameplayReady);
        Assert.False(Current(state)!.CanProveLocalOwnership);
    }

    [Theory]
    [InlineData("world")]
    [InlineData("binding")]
    [InlineData("revision")]
    [InlineData("stale")]
    [InlineData("source")]
    [InlineData("future")]
    public void InvalidPairCannotReplaceEarlierBasic(string invalid)
    {
        var state = WithOldBasic();
        var pair = Basic(invalid == "revision" ? 1 : 2, invalid == "world" ? OldWorld : FinalWorld,
            invalid == "stale" ? Now.AddSeconds(-3) : invalid == "future" ? Now.AddSeconds(1) : Now,
            invalid == "binding" ? new string('D', 64) : null,
            invalid == "source" ? DiagnosticInventoryObservation.SourceName : null);
        Assert.False(Accept(state, Full(pair)));
        Assert.Equal(OldWorld, Current(state)!.WorldStampFingerprint);
        Assert.Equal(2, Current(state)!.Counts["rawcode:I10h"]);
    }

    [Fact]
    public void StaleFullCannotUseFreshPairToRenewItsTimestamp()
    {
        var state = WithOldBasic();
        Assert.False(Accept(state, Full(Basic(2, FinalWorld), Now.AddSeconds(-3))));
        Assert.Equal(OldWorld, Current(state)!.WorldStampFingerprint);
    }

    [Fact]
    public void DelayedDeliveryExpiresTheWholePair()
    {
        var state = WithOldBasic();
        var result = Full(Basic(2, FinalWorld));
        Assert.False(state.TryAcceptFull(result, Now.AddSeconds(3), "2.320", Fingerprint, out _));
        Assert.Null(state.Current(Now.AddSeconds(3), "2.320", Fingerprint));
    }

    [Fact]
    public void SessionBoundaryClearsBothEvenWithAPair()
    {
        var state = WithOldBasic();
        Assert.False(Accept(state, Full(Basic(2, FinalWorld), state: RecognitionState.Waiting, boundary: true)));
        Assert.Null(Current(state));
    }

    [Fact]
    public void CompletedPairCanStartTheBasicLaneAndCannotBeReplayed()
    {
        var state = new DiagnosticInventoryPresentationState();
        var result = Full(Basic(1, FinalWorld));
        Assert.True(Accept(state, result));
        Assert.True(state.HasBasicFrames);
        state.Invalidate();
        Assert.False(Accept(state, result));
        Assert.Null(Current(state));
    }

    [Fact]
    public async Task BackpressureKeepsAcquiredCompletionPairTogetherAndOrdered()
    {
        var result = Full(Basic(3, FinalWorld));
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new DiagnosticInventoryPresentationState();
        var frames = DiagnosticRecognitionStream.Run<DiagnosticRecognitionFrame>((emit, _, token) =>
        {
            foreach (var revision in new long[] { 1, 2 })
            {
                var basic = Basic(revision, OldWorld);
                emit(DiagnosticRecognitionFrame.ForBasic(basic.Observation, basic.Diagnostics));
            }
            queued.SetResult();
            emit(DiagnosticRecognitionFrame.ForCompleted(result));
            token.ThrowIfCancellationRequested();
        }, CancellationToken.None);
        await using var iterator = frames.GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var revisions = new List<long>();
        do
        {
            if (iterator.Current.Basic is { } basic)
            {
                revisions.Add(basic.SourceRevision);
                Assert.True(state.TryAcceptBasic(basic, iterator.Current.Diagnostics, Now, "2.320", Fingerprint, out _));
            }
            else
            {
                Assert.Same(result, iterator.Current.CompletedResult);
                Assert.True(Accept(state, iterator.Current.CompletedResult!));
                Assert.Same(result.DiagnosticObservation, Current(state));
            }
        } while (await iterator.MoveNextAsync());
        Assert.Equal(new long[] { 1, 2 }, revisions);
        Assert.Equal(3, Current(state)!.Counts["rawcode:I10h"]);
    }

    [Fact]
    public void ProducerBuildsBasicFromUnprojectedValidatedSnapshotAndOriginalSampleTimes()
    {
        Assert.True(RawcodeCodec.TryParse("I10h", out var code));
        var snapshot = new Warcraft300Diagnostic.Inventory(new(0x10000, 0, 0x20000), 2, 2, 0,
            new Dictionary<uint, int> { [code] = 2 });
        var service = new WarcraftMemoryRecognitionService(Data.Value, Path.GetTempPath());
        var locator = new Warcraft300WorldLocator.Context(1, 2, 3, 4, 5, 6, 7, 0x30000, 0x40000);
        var sample = service.CreateBasicInventorySample(snapshot, locator, new MemoryProfile
            { MinimumCatalogMatchRatio = 0.6, RequireNonEmptyInventory = true },
            Diagnostics(DiagnosticBasicInventoryObservation.SourceName), "fixture-session", 1,
            Now.AddMilliseconds(-100), Now, TimeSpan.FromMilliseconds(100));
        Assert.Equal(DiagnosticInventoryAvailability.Ready, sample.Observation.Availability);
        Assert.Equal(2, sample.Observation.Counts["rawcode:I10h"]);
        Assert.Equal(Now.AddMilliseconds(-100), sample.Observation.StartedAt);
        Assert.Equal(Now, sample.Observation.CompletedAt);
        Assert.False(sample.Observation.GrowthAttributionAvailable);
        Assert.Empty(sample.Observation.GrowthReservedUnitIds);
        var same = service.CreateBasicInventorySample(snapshot, locator, new MemoryProfile
            { MinimumCatalogMatchRatio = 0.6, RequireNonEmptyInventory = true },
            sample.Diagnostics, "fixture-session", 2, Now.AddMilliseconds(-100), Now, TimeSpan.FromMilliseconds(100));
        Assert.Equal(sample.Observation.BindingContextId, same.Observation.BindingContextId);
        Assert.Equal(sample.Observation.WorldStampFingerprint, same.Observation.WorldStampFingerprint);
    }

    [Fact]
    public void RejectedSampleReportsItsFailureInsteadOfFreshInventory()
    {
        Assert.True(RawcodeCodec.TryParse("zzzz", out var unknown));
        var snapshot = new Warcraft300Diagnostic.Inventory(new(0x10000, 0, 0x20000), 1, 1, 0,
            new Dictionary<uint, int> { [unknown] = 1 });
        var service = new WarcraftMemoryRecognitionService(Data.Value, Path.GetTempPath());
        var sample = service.CreateBasicInventorySample(snapshot,
            new Warcraft300WorldLocator.Context(1, 2, 3, 4, 5, 6, 7, 0x30000, 0x40000),
            new MemoryProfile { MinimumCatalogMatchRatio = 0.6, RequireNonEmptyInventory = true },
            Diagnostics(DiagnosticBasicInventoryObservation.SourceName), "fixture-session", 1,
            Now.AddMilliseconds(-10), Now, TimeSpan.FromMilliseconds(10));
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, sample.Observation.Availability);
        Assert.Equal("Basic diagnostic mapping quality rejected", sample.Diagnostics.Detail);
    }
}
