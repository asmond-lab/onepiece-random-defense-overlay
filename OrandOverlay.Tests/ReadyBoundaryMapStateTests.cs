using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ReadyBoundaryMapStateTests
{
    [Fact]
    public void ConfirmedObjectiveRollbackDiscardsRound65PayloadBeforeNewSessionRound1()
    {
        var tracker = Tracker();
        var scanner = new IncrementalMapStateScanner(4096);
        scanner.Merge(new MapStateSample(65, 3, "악몽"));
        tracker.Observe(Snapshot("n006"));
        tracker.Observe(Snapshot("n006"));

        tracker.Observe(Snapshot("n000"));
        Assert.False(tracker.LastObservationConfirmedReset);
        var unconfirmed = Diagnostics(scanner.Current);
        Assert.Same(unconfirmed, WarcraftMemoryRecognitionService.ReadyDiagnostics(
            unconfirmed, tracker.LastObservationConfirmedReset));

        // Recognition captures diagnostics before observing the confirmed rollback.
        var beforeReset = Diagnostics(scanner.Current);
        var nextSignals = tracker.Observe(Snapshot("n000"));
        Assert.True(tracker.LastObservationConfirmedReset);
        Assert.Equal(1, nextSignals.ActiveObjectiveOrdinal);
        Assert.Equal(0, nextSignals.CompletedStoryStageOrdinal);
        scanner.Reset();
        Assert.Equal(0, scanner.Current.MaxRound);
        Assert.Equal(65, beforeReset.MapState!.Value.MaxRound);

        var boundary = WarcraftMemoryRecognitionService.ReadyDiagnostics(
            beforeReset, tracker.LastObservationConfirmedReset);
        Assert.Null(boundary.MapState);
        AssertOtherDiagnosticsPreserved(beforeReset, boundary);

        scanner.Merge(new MapStateSample(1, 0, "보통"));
        tracker.Observe(Snapshot("n000"));
        Assert.False(tracker.LastObservationConfirmedReset);
        var next = Diagnostics(scanner.Current);
        Assert.Same(next, WarcraftMemoryRecognitionService.ReadyDiagnostics(
            next, tracker.LastObservationConfirmedReset));
        Assert.Equal(1, next.MapState!.Value.MaxRound);
        Assert.Equal(0, next.MapState.Value.SettlementCopies);
        Assert.Equal("보통", next.MapState.Value.Difficulty);
    }

    [Fact]
    public void ConfirmedBoundaryPreservesEveryNonMapDiagnosticWithoutMutatingInput()
    {
        var original = Diagnostics(new MapStateSample(65, 3, "악몽"));
        var boundary = WarcraftMemoryRecognitionService.ReadyDiagnostics(original, true);

        Assert.Null(boundary.MapState);
        Assert.Equal(65, original.MapState!.Value.MaxRound);
        AssertOtherDiagnosticsPreserved(original, boundary);
    }

    [Fact]
    public void NormalReadyKeepsGenuineRoundSettlementAndDifficulty()
    {
        var original = Diagnostics(new MapStateSample(65, 3, "악몽"));
        Assert.Same(original, WarcraftMemoryRecognitionService.ReadyDiagnostics(original, false));
    }

    private static void AssertOtherDiagnosticsPreserved(
        RecognitionDiagnostics expected, RecognitionDiagnostics actual)
    {
        foreach (var property in typeof(RecognitionDiagnostics).GetProperties()
                     .Where(property => property.Name != nameof(RecognitionDiagnostics.MapState)))
            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
    }

    private static RecognitionDiagnostics Diagnostics(MapStateSample sample) => new()
    {
        Source = "WarcraftMemory", ProcessVersion = "2.0.4", ProcessId = 123,
        ProfileId = "verified", ProfileRevision = 4, ProfileSource = "fixture",
        ResolvedListAddress = "0x12345678", ObservedObjects = 8, MappedObjects = 6,
        UnknownObjects = 2, ForeignObjects = 17, UnknownRawcodes = ["xxxx"],
        GrowthUnitIds = ["rawcode:540h"], Gorosei = GoroseiEffects.Options.First(x => x.Mode != GoroseiMode.None).Mode,
        MapState = sample, Detail = "snapshot diagnostics",
        AdaptivePlanningObservation = new AdaptivePlanningRecognitionObservation(
            AdaptivePlanningReadCounters.Empty, TimeSpan.FromMilliseconds(7), 4, 1)
    };

    private static MapSignalSnapshotTracker Tracker() => new(MapSignalRecognitionProfile.FromStory(
        MapStoryProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"))));

    private static MapSignalRawSnapshot Snapshot(string objective)
    {
        Assert.True(RawcodeCodec.TryParse(objective, out var rawcode));
        return new MapSignalRawSnapshot([rawcode], ImmutableArray<uint>.Empty);
    }
}
