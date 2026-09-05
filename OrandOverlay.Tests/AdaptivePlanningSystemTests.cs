using System.Collections.Immutable;
using System.Runtime.InteropServices;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptivePlanningSystemTests
{
    [Fact]
    public void ProductionCompositionReplaysEveryNamedStateAndAllFifteenOptions()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var host = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var frames = new List<AdaptivePlanningReplayFrame>();

        var incomplete = new[]
        {
            new InventoryEntry { UnitId = fixture.Rare.Id, Count = 1 }
        };
        frames.Add(host.Apply(fixture.Source(5, PlannerPhase.AwaitFirstRare,
            stage: 6, rareWisps: 1, inventory: incomplete))!);
        frames.Add(host.Apply(fixture.Source(6, host.State.Phase,
            stage: 7, inventory: incomplete))!);
        var fallbackLegendId = frames[1].Applied.SuggestedLegendId;
        Assert.NotNull(fallbackLegendId);
        var observedInventory = new[]
        {
            new InventoryEntry { UnitId = fixture.Rare.Id, Count = 1 },
            new InventoryEntry { UnitId = fallbackLegendId!, Count = 1 }
        };
        frames.Add(host.Apply(fixture.Source(6, host.State.Phase, stage: 7,
            inventory: observedInventory))!);
        frames.Add(host.Apply(fixture.Source(20, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 1,
            inventory: observedInventory,
            previouslyObservedLegendIds: [fallbackLegendId!]))!);
        frames.Add(host.Apply(fixture.Source(20, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0,
            inventory: observedInventory,
            previouslyObservedLegendIds: [fallbackLegendId!]))!);
        frames.Add(host.Apply(fixture.Source(21, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0,
            inventory: observedInventory,
            previouslyObservedLegendIds: [fallbackLegendId!]))!);
        frames.Add(host.Apply(fixture.Source(24, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0,
            inventory: observedInventory,
            previouslyObservedLegendIds: [fallbackLegendId!]))!);
        var lockedGoal = host.State.RouteLock?.GoalUnitId;
        var lockedNavigation = host.State.NavigationLockId;
        frames.Add(host.Apply(fixture.Source(25, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0) with
        {
            Inventory =
            [
                new InventoryEntry { UnitId = fallbackLegendId!, Count = 2 },
                new InventoryEntry { UnitId = fixture.Rare.Id, Count = 2 }
            ]
        })!);
        host.LatchManualNavigationOverride();
        frames.Add(host.Apply(fixture.Source(25, host.State.Phase,
            stage: 9, latches: host.ManualLatches,
            inventory: observedInventory,
            previouslyObservedLegendIds: [fallbackLegendId!]))!);

        Assert.Contains(AdaptiveBuildBlocker.NoLegendCandidate,
            frames[0].Applied.Blockers);
        Assert.All(frames[0].BuildSnapshot.LegendCandidates, candidate =>
        {
            Assert.False(candidate.CardAllocationComplete);
            Assert.Equal(ResourceCompletion.Incomplete, candidate.Resources);
        });
        Assert.Null(frames[0].Applied.SuggestedLegendId);
        Assert.Null(frames[0].Applied.State.RouteLock);
        Assert.Equal(PlannerPhase.ChooseLegend, frames[1].Applied.State.Phase);
        var expectedFallback = frames[1].BuildSnapshot.LegendCandidates
            .OrderBy(candidate => candidate.MissingLeaves)
            .ThenByDescending(candidate => candidate.PreservedRouteCount)
            .ThenBy(candidate => candidate.UnitId, StringComparer.Ordinal)
            .First().UnitId;
        Assert.Equal(expectedFallback, fallbackLegendId);
        Assert.Contains(AdaptiveBuildBlocker.LegendFallback, frames[1].Applied.Blockers);
        Assert.Equal(fallbackLegendId, frames[2].Applied.State.LockedFirstLegendId);
        Assert.Equal(FirstLegendHistory.Observed, frames[2].Applied.State.FirstLegendHistory);
        Assert.Contains(AdaptiveBuildBlocker.UnspentRareWisps, frames[3].Applied.Blockers);
        Assert.Equal(fallbackLegendId, frames[3].Applied.State.LockedFirstLegendId);
        Assert.Equal(PlannerEvidenceState.Round20Preview, frames[4].Presentation.State);
        Assert.Equal(PlannerEvidenceState.Round21Actionable, frames[5].Presentation.State);
        Assert.NotEqual(NavigationRecommendationState.SourceExpectedForced,
            frames[6].Applied.Navigation.State);
        Assert.NotEmpty(frames[6].Applied.Navigation.Options);
        Assert.Contains(frames[6].Applied.Navigation.Options,
            option => option.OptionId == frames[6].Applied.Navigation.RecommendedOptionId);
        Assert.Equal(lockedGoal, frames[7].Applied.State.RouteLock?.GoalUnitId);
        Assert.Equal(lockedNavigation, frames[7].Applied.State.NavigationLockId);
        Assert.Equal(PlannerEvidenceState.ManualOverride, frames[8].Presentation.State);
        Assert.All(frames, frame => Assert.Equal(NavigationProfiles.Options.Select(x => x.Id),
            frame.OptionIds));
        Assert.All(frames, frame =>
        {
            Assert.Equal(AdaptivePlanningSystemBaselineTests.ProfilePin, frame.ProfileHash);
            Assert.Equal(AdaptivePlanningSystemBaselineTests.DataPin, frame.DataHash);
        });
        Assert.Equal(frames.Count, frames.Select(frame => frame.InputFingerprint)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(frames, frame => Assert.True(frame.Presentation.IsRecommendationOnly));

        host.ConfirmReset(1);

        Assert.Equal(PlannerPhase.AwaitFirstRare, host.State.Phase);
        Assert.Null(host.State.RouteLock);
        Assert.Null(host.State.NavigationLockId);
        Assert.Equal(ManualLatches.None, host.ManualLatches);
    }

    [Fact]
    public async Task ReplayApplicationSignalsExactCompletionEventWithoutFixedSleep()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var host = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var scheduled = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new TaskCompletionSource<AdaptivePlanningReplayFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(host.TryApply(fixture.Source(20, PlannerPhase.SpendRares),
            action => scheduled.TrySetResult(action),
            frame => applied.TrySetResult(frame)));
        var transaction = await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(applied.Task.IsCompleted);

        transaction();

        var frame = await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(frame.InputFingerprint, frame.Applied.InputFingerprint);
    }

    [Fact]
    public async Task SupersededReplayEventCannotApplyAfterLatestEvent()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var host = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var olderScheduled = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestScheduled = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestApplied = new TaskCompletionSource<AdaptivePlanningReplayFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var olderWrites = 0;

        Assert.True(host.TryApply(fixture.Source(20, PlannerPhase.SpendRares),
            action => olderScheduled.TrySetResult(action), _ => olderWrites++));
        Assert.True(host.TryApply(fixture.Source(21, PlannerPhase.SpendRares),
            action => latestScheduled.TrySetResult(action),
            frame => latestApplied.TrySetResult(frame)));
        var older = await olderScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = await latestScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        older();
        Assert.Equal(0, olderWrites);
        Assert.Null(host.LastApplied);
        latest();

        var frame = await latestApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(frame.Applied, host.LastApplied);
    }

    [Fact]
    public void UnknownRuntimeSignalFailsClosedAndPreservesLastGood()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var host = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var good = host.Apply(fixture.Source(20, PlannerPhase.SpendRares))!;
        var writes = 0;
        var unknown = fixture.Source(21, host.State.Phase) with
        {
            AdditionalValues = [PlanningValue.Unknown("current-wave")]
        };

        Assert.True(host.TryApply(unknown, action => action(), _ => writes++));

        Assert.Equal(0, writes);
        Assert.Same(good.Applied, host.LastApplied);
    }

    [Fact]
    public void CorruptedMechanicsPinIsRejectedWithoutReplacingSafeHost()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var safe = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var good = safe.Apply(fixture.Source(20, PlannerPhase.SpendRares))!;
        var directory = CopyProfileSet(fixture.DataDirectory);
        try
        {
            var path = Path.Combine(directory, "navigation-mechanics-2314.json");
            var bytes = File.ReadAllBytes(path);
            bytes[^2] ^= 1;
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidDataException>(() =>
                new AdaptivePlanningReplayHost(directory));
            Assert.Same(good.Applied, safe.LastApplied);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ProductionReadOnlyMemoryObservationsFeedTheSharedPerformanceSample()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var host = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var bytes = GC.AllocateArray<byte>(64, pinned: true);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        AdaptivePlanningReadCounters reads;
        try
        {
            var address = checked((ulong)handle.AddrOfPinnedObject().ToInt64());
            using var memory = ReadOnlyProcessMemory.Open(Environment.ProcessId);
            _ = memory.Read(address, 8);
            using (memory.BeginReadChannel(AdaptivePlanningReadChannel.MapState))
                _ = memory.Read(address, 16);
            using (memory.BeginReadChannel(AdaptivePlanningReadChannel.SideChannel))
                _ = memory.Read(address, 4);
            reads = memory.SnapshotReads();
        }
        finally
        {
            handle.Free();
        }
        var observation = new AdaptivePlanningRecognitionObservation(reads,
            TimeSpan.FromMilliseconds(20), 4, 1);

        var frame = host.Apply(fixture.Source(20, PlannerPhase.SpendRares),
            observation)!;

        Assert.Equal(8, frame.Performance.Reads.UnitTraversal.Bytes);
        Assert.Equal(1, frame.Performance.Reads.UnitTraversal.Calls);
        Assert.Equal(16, frame.Performance.Reads.MapState.Bytes);
        Assert.Equal(1, frame.Performance.Reads.MapState.Calls);
        Assert.Equal(4, frame.Performance.Reads.SideChannel.Bytes);
        Assert.Equal(1, frame.Performance.Reads.SideChannel.Calls);
        Assert.All(new[] { frame.Performance.Reads.UnitTraversal.Elapsed,
                frame.Performance.Reads.MapState.Elapsed,
                frame.Performance.Reads.SideChannel.Elapsed },
            elapsed => Assert.True(elapsed >= TimeSpan.Zero));
        Assert.Equal(LatestBackgroundWorkCoordinator.DefaultSettleDelay,
            frame.Performance.SettleElapsed);
        Assert.True(frame.Performance.ScoringElapsed >= TimeSpan.Zero);
        Assert.True(frame.Performance.TotalElapsed <= TimeSpan.FromMilliseconds(350));
        Assert.True(frame.Performance.IsWithinBudget);
    }

    [Fact]
    public void PerformanceGateEnforcesEveryChannelByteCallAndTimeBudget()
    {
        var allowed = new AdaptivePlanningReadMetric(1, 1, TimeSpan.FromMilliseconds(1));
        AdaptivePlanningPerformanceSample Sample(AdaptivePlanningReadMetric unit,
            AdaptivePlanningReadMetric map, AdaptivePlanningReadMetric side,
            long sideBytes = 1, long sideCalls = 1, TimeSpan? total = null) => new(
            new AdaptivePlanningReadCounters(unit, map, side), TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.Zero, total ?? TimeSpan.Zero, sideBytes, sideCalls);

        Assert.False(Sample(new(AdaptivePlanningPerformanceSample.UnitTraversalByteBudget + 1,
            1, TimeSpan.Zero), allowed, allowed).IsWithinBudget);
        Assert.False(Sample(new(1,
            AdaptivePlanningPerformanceSample.UnitTraversalCallBudget + 1, TimeSpan.Zero),
            allowed, allowed).IsWithinBudget);
        Assert.False(Sample(new(1, 1, TimeSpan.FromMilliseconds(351)),
            allowed, allowed).IsWithinBudget);
        Assert.False(Sample(allowed, new(AdaptivePlanningPerformanceSample.MapStateByteBudget + 1,
            1, TimeSpan.Zero), allowed).IsWithinBudget);
        Assert.False(Sample(allowed, new(1,
            AdaptivePlanningPerformanceSample.MapStateCallBudget + 1, TimeSpan.Zero),
            allowed).IsWithinBudget);
        Assert.False(Sample(allowed, new(1, 1, TimeSpan.FromMilliseconds(351)),
            allowed).IsWithinBudget);
        Assert.False(Sample(allowed, allowed, new(2, 1, TimeSpan.Zero)).IsWithinBudget);
        Assert.False(Sample(allowed, allowed, new(1, 2, TimeSpan.Zero)).IsWithinBudget);
        Assert.False(Sample(allowed, allowed, new(1, 1,
            TimeSpan.FromMilliseconds(351))).IsWithinBudget);
        Assert.False(Sample(allowed, allowed, allowed,
            total: TimeSpan.FromMilliseconds(351)).IsWithinBudget);
    }

    private static string CopyProfileSet(string source)
    {
        var target = Path.Combine(Path.GetTempPath(), $"adaptive-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(target);
        foreach (var name in new[]
        {
            "navigation-mechanics-2314.json", "game-data.demo.json",
            "map-recipe-overrides-2314.txt", "tmo-unit-catalog.json",
            "tmo-unit-additions-42479.json"
        })
            File.Copy(Path.Combine(source, name), Path.Combine(target, name));
        return target;
    }
}
