using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptiveBuildStateMachineTests
{
    [Fact]
    public void FirstRareIsLatchedWithoutSelectingAGoal()
    {
        var result = AdaptiveBuildStateMachine.Advance(AdaptiveBuildState.Initial(1), Snapshot(firstRare: true));

        Assert.Equal(PlannerPhase.AccumulateSpecialUncommon, result.State.Phase);
        Assert.Equal(FirstRareHistory.Observed, result.State.FirstRareHistory);
        Assert.Null(result.State.RouteLock);
        Assert.Null(result.SuggestedLegendId);
    }

    [Fact]
    public void ActiveSixRequiresSpentWispsCardsAndSeparatelyKnownResources()
    {
        var state = AdvanceToAccumulation();
        var unknown = AdaptiveBuildStateMachine.Advance(state, Snapshot(stage: 6, specialUncommonWisps: 0,
            legends: [Legend("complete", cardComplete: true, ResourceCompletion.Unknown, 0, 5)]));
        var incomplete = AdaptiveBuildStateMachine.Advance(state, Snapshot(stage: 6, specialUncommonWisps: 0,
            legends: [Legend("incomplete", cardComplete: false, ResourceCompletion.Complete, 1, 5)]));
        var complete = AdaptiveBuildStateMachine.Advance(state, Snapshot(stage: 6, specialUncommonWisps: 0,
            legends: [Legend("ready", cardComplete: true, ResourceCompletion.Complete, 0, 5)]));

        Assert.Contains(AdaptiveBuildBlocker.UnknownLegendResources, unknown.Blockers);
        Assert.Null(unknown.SuggestedLegendId);
        Assert.Null(incomplete.SuggestedLegendId);
        Assert.Equal("ready", complete.SuggestedLegendId);
        Assert.Equal(PlannerPhase.ChooseLegend, complete.State.Phase);
    }

    [Fact]
    public void ActiveSevenUsesNearestThenMostOptionPreservingFallback()
    {
        var result = AdaptiveBuildStateMachine.Advance(AdvanceToAccumulation(), Snapshot(stage: 7,
            specialUncommonWisps: 0, legends:
            [
                Legend("later", false, ResourceCompletion.Unknown, 2, 10),
                Legend("winner", false, ResourceCompletion.Unknown, 1, 7),
                Legend("less-options", false, ResourceCompletion.Unknown, 1, 3)
            ]));

        Assert.Equal(PlannerPhase.ChooseLegend, result.State.Phase);
        Assert.Equal("winner", result.SuggestedLegendId);
        Assert.Contains(AdaptiveBuildBlocker.LegendFallback, result.Blockers);
    }

    [Fact]
    public void LegendLocksOnlyAfterObservedOutputAndSimultaneousOutputsRemainPossible()
    {
        var choosing = AdaptiveBuildStateMachine.Advance(AdvanceToAccumulation(), Snapshot(stage: 7,
            specialUncommonWisps: 0, legends: [Legend("suggested", false, ResourceCompletion.Complete, 1, 4)])).State;
        var notObserved = AdaptiveBuildStateMachine.Advance(choosing, Snapshot(stage: 7,
            specialUncommonWisps: 0, legends: [Legend("suggested", false, ResourceCompletion.Complete, 1, 4)]));
        var simultaneous = AdaptiveBuildStateMachine.Advance(choosing, Snapshot(stage: 7,
            specialUncommonWisps: 0, observedLegends: ["b", "a"]));

        Assert.Null(notObserved.State.LockedFirstLegendId);
        Assert.Equal(PlannerPhase.ChooseLegend, notObserved.State.Phase);
        Assert.Null(simultaneous.State.LockedFirstLegendId);
        Assert.Equal(new[] { "a", "b" }, simultaneous.State.PossibleFirstLegendIds.ToArray());
        Assert.Equal(FirstLegendHistory.Ambiguous, simultaneous.State.FirstLegendHistory);
        Assert.Equal(new BasisPointInterval(0, 2500),
            simultaneous.State.HistoryAbandonmentUncertaintyBp);
        Assert.Equal(PlannerPhase.AwaitMarineford, simultaneous.State.Phase);
    }

    [Fact]
    public void ActiveNineProvesMarinefordThenRareWispsMustBeKnownSpent()
    {
        var state = StateWithObservedLegend();
        var marineford = AdaptiveBuildStateMachine.Advance(state, Snapshot(stage: 9, rareWisps: null));
        var unspent = AdaptiveBuildStateMachine.Advance(marineford.State, Snapshot(stage: 9, rareWisps: 1));
        var spent = AdaptiveBuildStateMachine.Advance(unspent.State, Snapshot(stage: 9, rareWisps: 0));

        Assert.Equal(PlannerPhase.SpendRares, marineford.State.Phase);
        Assert.Contains(AdaptiveBuildBlocker.UnknownRareWisps, marineford.Blockers);
        Assert.Equal(PlannerPhase.SpendRares, unspent.State.Phase);
        Assert.Contains(AdaptiveBuildBlocker.UnspentRareWisps, unspent.Blockers);
        Assert.Equal(PlannerPhase.CommitRound20, spent.State.Phase);
    }

    [Fact]
    public void LateAttachHistoryMustBeRobustAndRoundMayJumpFromNineteenToTwentyOne()
    {
        var late = AdaptiveBuildStateMachine.Advance(AdaptiveBuildState.Initial(1), Snapshot(round: 19, stage: 9,
            specialUncommonWisps: 0, rareWisps: 0));
        var blocked = AdaptiveBuildStateMachine.Advance(late.State, Snapshot(round: 21, stage: 9,
            specialUncommonWisps: 0, rareWisps: 0, route: Route("goal-a", robustHistory: false),
            navigation: Navigation("nav-a", robust: true)));
        var committed = AdaptiveBuildStateMachine.Advance(blocked.State, Snapshot(round: 21, stage: 9,
            specialUncommonWisps: 0, rareWisps: 0, route: Route("goal-a", robustHistory: true),
            navigation: Navigation("nav-a", robust: true)));

        Assert.Equal(FirstRareHistory.Unknown, late.State.FirstRareHistory);
        Assert.Equal(new BasisPointInterval(0, 2500), late.State.HistoryAbandonmentUncertaintyBp);
        Assert.Contains(AdaptiveBuildBlocker.UnknownFirstLegendHistory, blocked.Blockers);
        Assert.Null(blocked.State.RouteLock);
        Assert.Equal(PlannerPhase.Committed, committed.State.Phase);
        Assert.Equal("goal-a", committed.State.RouteLock!.GoalUnitId);
        Assert.Equal("nav-a", committed.State.NavigationLockId);
    }

    [Fact]
    public void RouteLocksExactlyOnceAndOnlyProgressRecascades()
    {
        var ready = StateReadyToCommit();
        var first = AdaptiveBuildStateMachine.Advance(ready, Snapshot(round: 20, stage: 9, rareWisps: 0,
            route: Route("goal-a", progress: 100), navigation: Navigation("preview", true)));
        var later = AdaptiveBuildStateMachine.Advance(first.State, Snapshot(round: 21, stage: 9, rareWisps: 0,
            route: Route("goal-b", progress: 900), navigation: Navigation("locked", true)));
        var recascade = AdaptiveBuildStateMachine.Advance(later.State, Snapshot(round: 22, stage: 9, rareWisps: 0,
            route: Route("goal-a", progress: 700), navigation: Navigation("different", true)));

        Assert.Equal("goal-a", first.State.RouteLock!.GoalUnitId);
        Assert.Null(first.State.NavigationLockId);
        Assert.Equal("preview", first.ProvisionalNavigationId);
        Assert.Equal("goal-a", later.State.RouteLock!.GoalUnitId);
        Assert.Equal(100, later.State.RouteLock.ProgressBp);
        Assert.Equal("locked", later.State.NavigationLockId);
        Assert.Equal(700, recascade.State.RouteLock!.ProgressBp);
        Assert.Equal("locked", recascade.State.NavigationLockId);
    }

    [Fact]
    public void ManualLatchesAreIndependentAndOnlyConfirmedResetClearsThem()
    {
        var initial = AdaptiveBuildState.Initial(1);
        var goal = AdaptiveBuildStateMachine.Advance(initial, Snapshot(manual: new ManualLatches(true, false)));
        var both = AdaptiveBuildStateMachine.Advance(goal.State, Snapshot(manual: new ManualLatches(false, true)));
        var unconfirmed = AdaptiveBuildStateMachine.Advance(both.State, Snapshot(reset: false));
        var reset = AdaptiveBuildStateMachine.Advance(unconfirmed.State, Snapshot(generation: 2, reset: true));

        Assert.True(goal.State.ManualLatches.GoalOverride);
        Assert.False(goal.State.ManualLatches.NavigationOverride);
        Assert.Equal(new ManualLatches(true, true), both.State.ManualLatches);
        Assert.Equal(new ManualLatches(true, true), unconfirmed.State.ManualLatches);
        Assert.Equal(ManualLatches.None, reset.State.ManualLatches);
        Assert.Equal(PlannerPhase.AwaitFirstRare, reset.State.Phase);
    }

    [Fact]
    public void ManualGoalAndNavigationBlockOnlyTheirOwnAutomation()
    {
        var routeBlocked = AdaptiveBuildStateMachine.Advance(StateReadyToCommit() with
        {
            ManualLatches = new ManualLatches(true, false)
        }, Snapshot(round: 21, stage: 9, rareWisps: 0, route: Route("goal", true), navigation: Navigation("nav", true)));
        var navBlocked = AdaptiveBuildStateMachine.Advance(StateReadyToCommit() with
        {
            ManualLatches = new ManualLatches(false, true)
        }, Snapshot(round: 21, stage: 9, rareWisps: 0, route: Route("goal", true), navigation: Navigation("nav", true)));

        Assert.Null(routeBlocked.State.RouteLock);
        Assert.Contains(AdaptiveBuildBlocker.ManualGoalOverride, routeBlocked.Blockers);
        Assert.NotNull(navBlocked.State.RouteLock);
        Assert.Null(navBlocked.State.NavigationLockId);
        Assert.Contains(AdaptiveBuildBlocker.ManualNavigationOverride, navBlocked.Blockers);
    }

    [Fact]
    public void TransientSnapshotFreezesEveryDecision()
    {
        var state = StateReadyToCommit();
        var result = AdaptiveBuildStateMachine.Advance(state, Snapshot(round: 21, stage: 9, rareWisps: 0,
            route: Route("goal", true), navigation: Navigation("nav", true), transient: true));

        Assert.Same(state, result.State);
        Assert.Contains(AdaptiveBuildBlocker.TransientSnapshot, result.Blockers);
    }

    [Fact]
    public void UnknownStoryAndUnconfirmedGenerationChangeFailClosed()
    {
        var accumulating = AdvanceToAccumulation();
        var unknownStory = AdaptiveBuildStateMachine.Advance(accumulating,
            Snapshot(specialUncommonWisps: 0));
        var otherGeneration = AdaptiveBuildStateMachine.Advance(accumulating,
            Snapshot(generation: 2, stage: 9, specialUncommonWisps: 0, rareWisps: 0));

        Assert.Equal(accumulating, unknownStory.State);
        Assert.Contains(AdaptiveBuildBlocker.UnknownStoryStage, unknownStory.Blockers);
        Assert.Same(accumulating, otherGeneration.State);
        Assert.Contains(AdaptiveBuildBlocker.MatchGenerationMismatch, otherGeneration.Blockers);
    }

    [Fact]
    public void EveryUnmetPhasePrerequisiteHasATypedBlocker()
    {
        var initial = AdaptiveBuildState.Initial(1);
        var noRare = AdaptiveBuildStateMachine.Advance(initial, Snapshot());
        var unspentEarly = AdaptiveBuildStateMachine.Advance(AdvanceToAccumulation(),
            Snapshot(stage: 5, specialUncommonWisps: 1));
        var beforeMarineford = AdaptiveBuildStateMachine.Advance(StateWithObservedLegend(),
            Snapshot(stage: 8, rareWisps: 0));
        var beforeRound20 = AdaptiveBuildStateMachine.Advance(StateReadyToCommit(),
            Snapshot(round: 19, stage: 9, rareWisps: 0, route: Route("goal", true)));
        var noRoute = AdaptiveBuildStateMachine.Advance(StateReadyToCommit(),
            Snapshot(round: 20, stage: 9, rareWisps: 0));

        Assert.Contains(AdaptiveBuildBlocker.AwaitingFirstRare, noRare.Blockers);
        Assert.Contains(AdaptiveBuildBlocker.UnspentSpecialUncommonWisps, unspentEarly.Blockers);
        Assert.Contains(AdaptiveBuildBlocker.AwaitingMarineford, beforeMarineford.Blockers);
        Assert.Contains(AdaptiveBuildBlocker.AwaitingRound20, beforeRound20.Blockers);
        Assert.Contains(AdaptiveBuildBlocker.NoRouteCandidate, noRoute.Blockers);
    }

    [Fact]
    public void NavigationRequiresRobustEvidenceAndNeverLocksOutsideRoundsTwentyOneToTwentyThree()
    {
        var committed = AdaptiveBuildStateMachine.Advance(StateReadyToCommit(),
            Snapshot(round: 20, stage: 9, rareWisps: 0, route: Route("goal", true))).State;
        var nonRobust = AdaptiveBuildStateMachine.Advance(committed,
            Snapshot(round: 21, stage: 9, rareWisps: 0, navigation: Navigation("unsafe", false)));
        var tooLate = AdaptiveBuildStateMachine.Advance(nonRobust.State,
            Snapshot(round: 24, stage: 9, rareWisps: 0, navigation: Navigation("late", true)));

        Assert.Contains(AdaptiveBuildBlocker.NavigationNotRobust, nonRobust.Blockers);
        Assert.Null(nonRobust.State.NavigationLockId);
        Assert.Null(tooLate.State.NavigationLockId);
        Assert.Null(tooLate.ProvisionalNavigationId);
    }

    [Fact]
    public void RoundTwentyNavigationIsProvisionalEvenWhenNotRobust()
    {
        var result = AdaptiveBuildStateMachine.Advance(StateReadyToCommit(),
            Snapshot(round: 20, stage: 9, rareWisps: 0, route: Route("goal", true),
                navigation: Navigation("preview", false)));

        Assert.Equal(PlannerPhase.Committed, result.State.Phase);
        Assert.Equal("preview", result.ProvisionalNavigationId);
        Assert.Null(result.State.NavigationLockId);
        Assert.DoesNotContain(AdaptiveBuildBlocker.NavigationNotRobust, result.Blockers);
    }

    private static AdaptiveBuildState AdvanceToAccumulation() =>
        AdaptiveBuildStateMachine.Advance(AdaptiveBuildState.Initial(1), Snapshot(firstRare: true)).State;

    private static AdaptiveBuildState StateWithObservedLegend()
    {
        var choosing = AdaptiveBuildStateMachine.Advance(AdvanceToAccumulation(), Snapshot(stage: 7,
            specialUncommonWisps: 0, legends: [Legend("legend", false, ResourceCompletion.Complete, 1, 1)])).State;
        return AdaptiveBuildStateMachine.Advance(choosing, Snapshot(stage: 7,
            specialUncommonWisps: 0, observedLegends: ["legend"])).State;
    }

    private static AdaptiveBuildState StateReadyToCommit() => StateWithObservedLegend() with
    {
        Phase = PlannerPhase.CommitRound20,
        MarinefordConfirmed = true
    };

    private static LegendBuildCandidate Legend(string id, bool cardComplete, ResourceCompletion resources,
        int missingLeaves, int preserving) => new(id, cardComplete, resources, missingLeaves, preserving);

    private static AdaptiveRouteLockCandidate Route(string goal, bool robustHistory = true, int progress = 500) =>
        new(DamageLane.Physical, goal, "package", progress, robustHistory);

    private static AdaptiveNavigationCandidate Navigation(string id, bool robust) => new(id, robust);

    private static AdaptiveBuildSnapshot Snapshot(long generation = 1, int round = 1, int? stage = null,
        bool firstRare = false, int? specialUncommonWisps = null, int? rareWisps = null,
        ImmutableArray<LegendBuildCandidate> legends = default, ImmutableArray<string> observedLegends = default,
        AdaptiveRouteLockCandidate? route = null, AdaptiveNavigationCandidate? navigation = null,
        ManualLatches manual = default, bool transient = false, bool reset = false) =>
        new(generation, round, stage, firstRare, specialUncommonWisps, rareWisps, legends,
            observedLegends, route, navigation, manual, transient, reset);
}
