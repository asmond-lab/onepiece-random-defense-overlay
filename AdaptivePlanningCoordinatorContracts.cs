using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record AdaptivePlanningRefreshInput
{
    public AdaptivePlanningInput CanonicalInput { get; }
    public AdaptiveBuildSnapshot BuildSnapshot { get; }
    public NavigationIntervalScoringRequest NavigationRequest { get; }
    public string Fingerprint { get; }

    public AdaptivePlanningRefreshInput(AdaptivePlanningInput canonicalInput,
        AdaptiveBuildSnapshot buildSnapshot,
        NavigationIntervalScoringRequest navigationRequest)
    {
        CanonicalInput = canonicalInput ?? throw new ArgumentNullException(nameof(canonicalInput));
        BuildSnapshot = buildSnapshot ?? throw new ArgumentNullException(nameof(buildSnapshot));
        NavigationRequest = navigationRequest ?? throw new ArgumentNullException(nameof(navigationRequest));
        if (canonicalInput.MatchGeneration != buildSnapshot.MatchGeneration ||
            canonicalInput.Round != buildSnapshot.Round || canonicalInput.Round != navigationRequest.Round)
            throw new ArgumentException("Planner inputs must describe the same match and round.");
        if (canonicalInput.ManualLatches != buildSnapshot.ManualLatches)
            throw new ArgumentException("Planner inputs must carry the same manual latches.");
        Fingerprint = AdaptivePlanningCoordinatorFingerprint.Create(this);
    }
}

public sealed record AdaptivePlanningWork(
    long Sequence,
    string InputFingerprint,
    AdaptivePlanningRefreshInput Input,
    AdaptiveBuildState BaselineState,
    NavigationIntervalScoringResult? PreviousNavigation);

public sealed record AdaptivePlanningComputed(
    long Sequence,
    string InputFingerprint,
    AdaptiveBuildTransition Transition,
    NavigationIntervalScoringResult Navigation,
    AdaptiveDecisionEvent Trace,
    bool PreserveLastGood);

public sealed record AdaptivePlanningApplied(
    string InputFingerprint,
    AdaptiveBuildState State,
    NavigationIntervalScoringResult Navigation,
    AdaptiveDecisionEvent Trace)
{
    public string? SuggestedGoalId => null;
    public string? SuggestedLegendId { get; init; }
    public ImmutableArray<AdaptiveBuildBlocker> Blockers { get; init; } = [];
}
