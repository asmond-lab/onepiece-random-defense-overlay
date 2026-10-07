using System.Collections.Immutable;

namespace OrandOverlay;

public sealed class AdaptivePlanningCoordinator
{
    private readonly object _gate = new();
    private AdaptiveBuildState _state;
    private ManualLatches _manualLatches;
    private NavigationIntervalScoringResult? _lastNavigation;
    private AdaptivePlanningApplied? _lastApplied;
    private string? _currentFingerprint;
    private long _sequence;
    private long _computationCount;

    public AdaptivePlanningCoordinator() : this(AdaptiveBuildState.Initial(0)) { }

    internal AdaptivePlanningCoordinator(AdaptiveBuildState initialState)
    {
        _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _manualLatches = initialState.ManualLatches;
    }

    public long MatchGeneration { get { lock (_gate) return _state.MatchGeneration; } }
    public AdaptiveBuildState State { get { lock (_gate) return _state; } }
    public ManualLatches ManualLatches { get { lock (_gate) return _manualLatches; } }
    public long ComputationCount => Interlocked.Read(ref _computationCount);
    public AdaptivePlanningApplied? LastApplied { get { lock (_gate) return _lastApplied; } }

    public AdaptivePlanningWork? TryBegin(AdaptivePlanningRefreshInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (_gate)
        {
            if (_currentFingerprint == input.Fingerprint) return null;
            if (input.CanonicalInput.MatchGeneration != _state.MatchGeneration)
                return null;
            _currentFingerprint = input.Fingerprint;
            Interlocked.Increment(ref _computationCount);
            return new AdaptivePlanningWork(++_sequence, input.Fingerprint, input,
                _state, _lastNavigation);
        }
    }

    public static AdaptivePlanningComputed Evaluate(AdaptivePlanningWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var request = work.Input.NavigationRequest with
        {
            ManualNavigationOverride = work.BaselineState.ManualLatches.NavigationOverride,
            LockedOverlayRecommendationId = work.BaselineState.NavigationLockId,
            PreviousResult = work.PreviousNavigation
        };
        var navigation = NavigationIntervalScorer.Score(request);
        var transition = AdaptiveBuildStateMachine.Advance(work.BaselineState,
            WithNavigation(work.Input.BuildSnapshot, navigation));
        var preserve = work.Input.BuildSnapshot.IsTransient ||
                       request.InputState != NavigationScoringInputState.Ready;
        return new AdaptivePlanningComputed(work.Sequence, work.InputFingerprint,
            transition, navigation, CreateTrace(work, transition, navigation), preserve);
    }

    private static AdaptiveBuildSnapshot WithNavigation(AdaptiveBuildSnapshot snapshot,
        NavigationIntervalScoringResult navigation)
    {
        var candidate = navigation.RecommendedOptionId is { } id &&
                        navigation.State is NavigationRecommendationState.Provisional or
                            NavigationRecommendationState.Actionable
            ? new AdaptiveNavigationCandidate(id,
                navigation.State == NavigationRecommendationState.Actionable &&
                navigation.ShouldLockOverlayRecommendation)
            : snapshot.NavigationCandidate;
        return new AdaptiveBuildSnapshot(snapshot.MatchGeneration, snapshot.Round,
            snapshot.ActiveStoryStage, snapshot.FirstRareObserved,
            snapshot.SpecialUncommonWispCount, snapshot.RareWispCount,
            snapshot.LegendCandidates, snapshot.NewlyObservedLegendIds,
            snapshot.RouteCandidate, candidate, snapshot.ManualLatches,
            snapshot.IsTransient, snapshot.IsConfirmedReset);
    }

    public void ScheduleApply(AdaptivePlanningComputed computed,
        Action<Action> schedule, Action<AdaptivePlanningApplied> mutate)
    {
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(mutate);
        schedule(() =>
        {
            lock (_gate)
            {
                if (!computed.InputFingerprint.Equals(_currentFingerprint,
                        StringComparison.Ordinal)) return;
                if (computed.PreserveLastGood) return;
                var applied = new AdaptivePlanningApplied(computed.InputFingerprint,
                    computed.Transition.State, computed.Navigation, computed.Trace)
                {
                    SuggestedLegendId = computed.Transition.SuggestedLegendId,
                    Blockers = computed.Transition.Blockers
                };
                _state = computed.Transition.State;
                _manualLatches = _state.ManualLatches;
                _lastNavigation = computed.Navigation;
                _lastApplied = applied;
                mutate(applied);
            }
        });
    }

    public void LatchManualGoalOverride()
    {
        lock (_gate) SetLatches(new ManualLatches(true, _manualLatches.NavigationOverride));
    }

    public void LatchManualNavigationOverride()
    {
        lock (_gate) SetLatches(new ManualLatches(_manualLatches.GoalOverride, true));
    }

    public void ClearManualNavigationOverride()
    {
        lock (_gate) SetLatches(new ManualLatches(_manualLatches.GoalOverride, false));
    }

    public void ClearManualGoalOverride()
    {
        lock (_gate) SetLatches(new ManualLatches(false, _manualLatches.NavigationOverride));
    }

    public void NoteProgrammaticSelection() { }

    public void ConfirmReset(long matchGeneration)
    {
        lock (_gate)
        {
            _state = AdaptiveBuildState.Initial(matchGeneration);
            _manualLatches = ManualLatches.None;
            _lastNavigation = null;
            _lastApplied = null;
            _currentFingerprint = null;
        }
    }

    private void SetLatches(ManualLatches value)
    {
        _manualLatches = value;
        _state = _state with { ManualLatches = value };
        _currentFingerprint = null;
    }

    private static AdaptiveDecisionEvent CreateTrace(AdaptivePlanningWork work,
        AdaptiveBuildTransition transition, NavigationIntervalScoringResult navigation)
    {
        var reasons = transition.Blockers.Select(ToReason)
            .Concat(navigation.Blockers.Select(ToReason)).Distinct().ToImmutableArray();
        if (reasons.IsDefaultOrEmpty) reasons = [ReasonCode.Ready];
        return new AdaptiveDecisionEvent(new AdaptiveDecisionEventInput(
            "adaptive-planning-v1", work.Input.CanonicalInput.ProfileHash,
            work.Input.CanonicalInput.DataHash, work.BaselineState.MatchGeneration,
            work.InputFingerprint, transition.State.Phase, reasons, [],
            navigation.RecommendedOptionId is { } id ? [id] : [],
            navigation.Options.IsDefaultOrEmpty ? 0 : navigation.Options.Max(item => item.ConfidenceBp),
            navigation.State == NavigationRecommendationState.SourceExpectedForced, [],
            ToRegime(navigation.Regime), transition.State.NavigationLockId is not null,
            transition.State.ManualLatches,
            navigation.State == NavigationRecommendationState.SourceExpectedForced
                ? navigation.RecommendedOptionId : null));
    }

    private static ReasonCode ToReason(AdaptiveBuildBlocker blocker) => blocker switch
    {
        AdaptiveBuildBlocker.ManualGoalOverride or AdaptiveBuildBlocker.ManualNavigationOverride =>
            ReasonCode.ManualOverride,
        AdaptiveBuildBlocker.TransientSnapshot => ReasonCode.StaleFingerprint,
        _ => ReasonCode.UnknownInput
    };

    private static ReasonCode ToReason(NavigationScoreBlocker blocker) => blocker switch
    {
        NavigationScoreBlocker.ManualOverride => ReasonCode.ManualOverride,
        NavigationScoreBlocker.ArithmeticLimitExceeded => ReasonCode.ArithmeticLimitExceeded,
        NavigationScoreBlocker.NoEligibleOptions or NavigationScoreBlocker.NonDominantIntervals =>
            ReasonCode.NoSafeRecommendation,
        _ => ReasonCode.UnknownInput
    };

    private static AdaptiveNavigationRegime ToRegime(NavigationScoringRegime regime) => regime switch
    {
        NavigationScoringRegime.SecureCore => AdaptiveNavigationRegime.SecureCore,
        NavigationScoringRegime.GuaranteedRecovery => AdaptiveNavigationRegime.GuaranteedRecovery,
        NavigationScoringRegime.DesperationRecovery => AdaptiveNavigationRegime.DesperationRecovery,
        _ => AdaptiveNavigationRegime.None
    };
}
