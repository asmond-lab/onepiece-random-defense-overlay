using System.Collections.Immutable;
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

        frames.Add(host.Apply(fixture.Source(5, PlannerPhase.AwaitFirstRare,
            stage: 5, specialWisps: 1, rareWisps: 1))!);
        frames.Add(host.Apply(fixture.Source(6, host.State.Phase,
            stage: 6, specialWisps: 0, rareWisps: 0))!);
        frames.Add(host.Apply(fixture.Source(20, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 1))!);
        frames.Add(host.Apply(fixture.Source(20, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0))!);
        frames.Add(host.Apply(fixture.Source(21, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0))!);
        frames.Add(host.Apply(fixture.Source(24, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0))!);
        var lockedGoal = host.State.RouteLock?.GoalUnitId;
        var lockedNavigation = host.State.NavigationLockId;
        frames.Add(host.Apply(fixture.Source(25, host.State.Phase,
            stage: 9, specialWisps: 0, rareWisps: 0) with
        {
            Inventory =
            [
                new InventoryEntry { UnitId = fixture.Legend.Id, Count = 2 },
                new InventoryEntry { UnitId = fixture.Rare.Id, Count = 2 }
            ]
        })!);
        host.LatchManualNavigationOverride();
        frames.Add(host.Apply(fixture.Source(25, host.State.Phase,
            stage: 9, latches: host.ManualLatches))!);

        Assert.Contains(AdaptiveBuildBlocker.UnspentSpecialUncommonWisps,
            frames[0].Applied.Blockers);
        Assert.Equal(PlannerPhase.ChooseLegend, frames[1].Applied.State.Phase);
        Assert.Contains(AdaptiveBuildBlocker.UnspentRareWisps, frames[2].Applied.Blockers);
        Assert.Equal(PlannerEvidenceState.Round20Preview, frames[3].Presentation.State);
        Assert.Equal(PlannerEvidenceState.Round21Actionable, frames[4].Presentation.State);
        Assert.Equal(NavigationRecommendationState.SourceExpectedForced,
            frames[5].Applied.Navigation.State);
        Assert.Equal("AlliedForces.DoubleBenefit",
            frames[5].Applied.Navigation.RecommendedOptionId);
        Assert.Equal(lockedGoal, frames[6].Applied.State.RouteLock?.GoalUnitId);
        Assert.Equal(lockedNavigation, frames[6].Applied.State.NavigationLockId);
        Assert.Equal(PlannerEvidenceState.ManualOverride, frames[7].Presentation.State);
        Assert.NotNull(frames[2].Applied.State.LockedFirstLegendId);
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
    public void FakeReadOnlyPerformanceCountersStaySeparatedAndUnderTotalGate()
    {
        var fixture = AdaptivePlanningSystemBaselineTests.Fixture();
        var host = new AdaptivePlanningReplayHost(fixture.DataDirectory);
        var reads = new AdaptivePlanningReadCounters(
            new AdaptivePlanningReadMetric(640, 10, TimeSpan.FromMilliseconds(4)),
            new AdaptivePlanningReadMetric(1_048_576, 16, TimeSpan.FromMilliseconds(18)),
            new AdaptivePlanningReadMetric(20, 5, TimeSpan.FromMilliseconds(2)));

        var frame = host.Apply(fixture.Source(20, PlannerPhase.SpendRares), reads,
            TimeSpan.FromMilliseconds(24))!;

        Assert.Equal(640, frame.Performance.Reads.UnitTraversal.Bytes);
        Assert.Equal(10, frame.Performance.Reads.UnitTraversal.Calls);
        Assert.Equal(TimeSpan.FromMilliseconds(4),
            frame.Performance.Reads.UnitTraversal.Elapsed);
        Assert.Equal(1_048_576, frame.Performance.Reads.MapState.Bytes);
        Assert.Equal(16, frame.Performance.Reads.MapState.Calls);
        Assert.Equal(TimeSpan.FromMilliseconds(18),
            frame.Performance.Reads.MapState.Elapsed);
        Assert.Equal(20, frame.Performance.Reads.SideChannel.Bytes);
        Assert.Equal(5, frame.Performance.Reads.SideChannel.Calls);
        Assert.Equal(TimeSpan.FromMilliseconds(2),
            frame.Performance.Reads.SideChannel.Elapsed);
        Assert.Equal(LatestBackgroundWorkCoordinator.DefaultSettleDelay,
            frame.Performance.SettleElapsed);
        Assert.True(frame.Performance.ScoringElapsed >= TimeSpan.Zero);
        Assert.True(frame.Performance.TotalElapsed <= TimeSpan.FromMilliseconds(350));
        Assert.True(frame.Performance.IsWithinBudget);
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
