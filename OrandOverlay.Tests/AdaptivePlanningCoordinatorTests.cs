using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AdaptivePlanningCoordinatorTests
{
    [Fact]
    public void FullCanonicalInputFingerprintIsDeterministicAndSemantic()
    {
        var first = Input(round: 20, recognitionRevision: 7);
        var replay = Input(round: 20, recognitionRevision: 7);
        var changedRecognition = Input(round: 20, recognitionRevision: 8);
        var changedCounter = Input(round: 20, recognitionRevision: 7, rareWisps: 1);

        Assert.Equal(first.Fingerprint, replay.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changedRecognition.Fingerprint);
        Assert.NotEqual(first.Fingerprint, changedCounter.Fingerprint);
    }

    [Fact]
    public void SameFingerprintSchedulesExactlyOneComputation()
    {
        var coordinator = new AdaptivePlanningCoordinator();
        var input = Input(round: 20);

        Assert.NotNull(coordinator.TryBegin(input));
        Assert.Null(coordinator.TryBegin(input));
        Assert.NotNull(coordinator.TryBegin(Input(round: 21)));
        Assert.Equal(2, coordinator.ComputationCount);
    }

    [Fact]
    public void OlderResultCompletingLateWritesNothingAndLatestAppliesAtomically()
    {
        var coordinator = new AdaptivePlanningCoordinator();
        var older = coordinator.TryBegin(Input(round: 20))!;
        var latest = coordinator.TryBegin(Input(round: 21))!;
        var schedules = 0;
        var writes = 0;

        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(older), action =>
        {
            schedules++;
            action();
        }, _ => writes++);
        Assert.Equal(0, writes);
        Assert.Null(coordinator.LastApplied);

        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(latest), action =>
        {
            schedules++;
            action();
        }, applied =>
        {
            writes++;
            Assert.Equal(latest.InputFingerprint, applied.InputFingerprint);
        });

        Assert.Equal(2, schedules);
        Assert.Equal(1, writes);
        Assert.Equal(latest.InputFingerprint, coordinator.LastApplied!.InputFingerprint);
    }

    [Fact]
    public void FirstRareOnlyAdvancesTheLatchAndNeverSelectsAGoal()
    {
        var coordinator = new AdaptivePlanningCoordinator();
        var work = coordinator.TryBegin(Input(round: 7, firstRare: true))!;
        AdaptivePlanningApplied? applied = null;

        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(work), action => action(),
            value => applied = value);

        Assert.NotNull(applied);
        Assert.Equal(PlannerPhase.AccumulateSpecialUncommon, applied.State.Phase);
        Assert.Null(applied.SuggestedGoalId);
    }

    [Fact]
    public void ManualLatchesAreIndependentAndOnlyConfirmedResetClearsThem()
    {
        var coordinator = new AdaptivePlanningCoordinator();

        coordinator.LatchManualGoalOverride();
        Assert.Equal(new ManualLatches(true, false), coordinator.ManualLatches);
        coordinator.NoteProgrammaticSelection();
        Assert.Equal(new ManualLatches(true, false), coordinator.ManualLatches);
        coordinator.LatchManualNavigationOverride();
        Assert.Equal(new ManualLatches(true, true), coordinator.ManualLatches);

        coordinator.ConfirmReset(1);

        Assert.Equal(ManualLatches.None, coordinator.ManualLatches);
        Assert.Equal(1, coordinator.MatchGeneration);
    }

    [Fact]
    public void NavigationLifecycleIsRecommendationOnlyAndNeverClaimsMapSelection()
    {
        var committed = AdaptiveBuildState.Initial(0) with
        {
            Phase = PlannerPhase.Committed,
            FirstRareHistory = FirstRareHistory.Observed,
            RouteLock = Route()
        };
        var coordinator = new AdaptivePlanningCoordinator(committed);

        var round20 = Apply(coordinator, Input(20, options: Options(10_000)));
        Assert.Equal(NavigationRecommendationState.Provisional, round20.Navigation.State);
        Assert.False(round20.Navigation.ClaimsRuntimeSelection);

        var round21 = Apply(coordinator, Input(21, options: Options(10_000)));
        Assert.Equal(NavigationRecommendationState.Actionable, round21.Navigation.State);
        Assert.True(round21.Navigation.ShouldLockOverlayRecommendation);
        Assert.False(round21.Navigation.ClaimsRuntimeSelection);
        Assert.Equal(round21.Navigation.RecommendedOptionId,
            round21.State.NavigationLockId);

        var locked = Apply(coordinator, Input(22, options: Options(10_000)));
        Assert.Same(round21.Navigation, locked.Navigation);
        Assert.Equal(round21.Navigation.RecommendedOptionId,
            locked.Navigation.RecommendedOptionId);

        coordinator.LatchManualNavigationOverride();
        var manual = Apply(coordinator, Input(23, options: Options(10_000),
            latches: coordinator.ManualLatches));
        Assert.Equal(NavigationRecommendationState.ManualOverride, manual.Navigation.State);

        var manualCoordinator = new AdaptivePlanningCoordinator(committed);
        manualCoordinator.LatchManualNavigationOverride();
        var manualWithoutLock = Apply(manualCoordinator, Input(21, options: Options(10_000),
            latches: manualCoordinator.ManualLatches));
        Assert.Equal(NavigationRecommendationState.ManualOverride,
            manualWithoutLock.Navigation.State);

        var forced = Apply(coordinator, Input(24, options: Options(10_000),
            latches: coordinator.ManualLatches));
        Assert.Equal(NavigationRecommendationState.SourceExpectedForced, forced.Navigation.State);
        Assert.Equal("AlliedForces.DoubleBenefit", forced.Navigation.RecommendedOptionId);
        Assert.False(forced.Navigation.ClaimsRuntimeSelection);
    }

    [Fact]
    public void TransientOrUnknownInputKeepsVisibleLastGoodUnchanged()
    {
        var coordinator = new AdaptivePlanningCoordinator();
        var good = Apply(coordinator, Input(7, firstRare: true));
        var writes = 0;
        var transient = Input(8, firstRare: true, transient: true);
        var work = coordinator.TryBegin(transient)!;

        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(work), action => action(),
            _ => writes++);

        Assert.Equal(0, writes);
        Assert.Same(good, coordinator.LastApplied);
    }

    private static AdaptivePlanningApplied Apply(AdaptivePlanningCoordinator coordinator,
        AdaptivePlanningRefreshInput input)
    {
        AdaptivePlanningApplied? applied = null;
        var work = coordinator.TryBegin(input)!;
        coordinator.ScheduleApply(AdaptivePlanningCoordinator.Evaluate(work), action => action(),
            value => applied = value);
        return applied!;
    }

    private static AdaptivePlanningRefreshInput Input(int round,
        long recognitionRevision = 1, int rareWisps = 0, bool firstRare = false,
        bool transient = false, ImmutableArray<NavigationIntervalOptionInput> options = default,
        ManualLatches latches = default)
    {
        var values = ImmutableArray.Create(
            PlanningValue.Known("recognition-revision", recognitionRevision),
            PlanningValue.Known("recognition-state", transient ? 1 : 0),
            PlanningValue.Known("rare-wisps", rareWisps),
            PlanningValue.Known("inventory:rare-a", firstRare ? 1 : 0));
        var canonical = new AdaptivePlanningInput(0, round, PlannerPhase.AwaitFirstRare,
            new StorySignal(1, ["stage-0"], !transient), values, latches,
            "profile-generation", "data-generation");
        var build = new AdaptiveBuildSnapshot(0, round, 1, firstRare, 0, rareWisps,
            [], [], null, new AdaptiveNavigationCandidate("AlliedForces.DoubleBenefit", true),
            latches, transient, false);
        var navigation = new NavigationIntervalScoringRequest
        {
            Round = round,
            BeforeBuildBp = 5_000,
            BeforeCoreBp = 10_000,
            BeforeCombatBp = 5_000,
            RouteConfidenceBp = 10_000,
            MechanicsConfidenceBp = 10_000,
            InputState = transient ? NavigationScoringInputState.Transient : NavigationScoringInputState.Ready,
            Options = options.IsDefault ? [] : options
        };
        return new AdaptivePlanningRefreshInput(canonical, build, navigation);
    }

    private static ImmutableArray<NavigationIntervalOptionInput> Options(int confidence) =>
        NavigationIntervalScorer.RequiredOptionIds.Select((id, ordinal) =>
            new NavigationIntervalOptionInput(id, ordinal, true, [confidence], [],
                NavigationRiskPosture.Balanced,
                [new NavigationCoupledScenario(id,
                    [new NavigationScoringOutcome(new Rational(1, 1),
                        5_000 + ordinal * 100, 10_000, 5_000 + ordinal * 100)])]))
            .ToImmutableArray();

    private static AdaptiveRouteLockCandidate Route() =>
        new(DamageLane.Physical, "goal", "package", 5_000, true);
}
