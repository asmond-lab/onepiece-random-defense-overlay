using System.Collections.Immutable;

namespace OrandOverlay;

public enum ResourceCompletion { Unknown, Incomplete, Complete }
public enum FirstRareHistory { NotObserved, Observed, Unknown }
public enum FirstLegendHistory { NotObserved, Observed, Ambiguous, Unknown }

public readonly record struct BasisPointInterval
{
    public int Lower { get; }
    public int Upper { get; }

    public BasisPointInterval(int lower, int upper)
    {
        if (lower is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(lower));
        if (upper is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(upper));
        if (lower > upper) throw new ArgumentException("Lower bound must not exceed upper bound.");
        Lower = lower;
        Upper = upper;
    }
}

public enum AdaptiveBuildBlocker
{
    AwaitingFirstRare, UnknownStoryStage, MatchGenerationMismatch, UnspentSpecialUncommonWisps,
    UnknownSpecialUncommonWisps, UnknownLegendResources, NoLegendCandidate, LegendFallback,
    AwaitingLegendOutput, AwaitingMarineford, UnknownRareWisps, UnspentRareWisps, AwaitingRound20,
    UnknownFirstLegendHistory, RouteNotRobust, NoRouteCandidate, NavigationNotRobust,
    ManualGoalOverride, ManualNavigationOverride, TransientSnapshot
}

public sealed record LegendBuildCandidate
{
    public string UnitId { get; }
    public bool CardAllocationComplete { get; }
    public ResourceCompletion Resources { get; }
    public int MissingLeaves { get; }
    public int PreservedRouteCount { get; }

    public LegendBuildCandidate(string unitId, bool cardAllocationComplete,
        ResourceCompletion resources, int missingLeaves, int preservedRouteCount)
    {
        if (string.IsNullOrWhiteSpace(unitId)) throw new ArgumentException("A legend ID is required.", nameof(unitId));
        if (missingLeaves < 0) throw new ArgumentOutOfRangeException(nameof(missingLeaves));
        if (preservedRouteCount < 0) throw new ArgumentOutOfRangeException(nameof(preservedRouteCount));
        UnitId = unitId;
        CardAllocationComplete = cardAllocationComplete;
        Resources = resources;
        MissingLeaves = missingLeaves;
        PreservedRouteCount = preservedRouteCount;
    }
}

public sealed record AdaptiveRouteLockCandidate
{
    public DamageLane Lane { get; }
    public string GoalUnitId { get; }
    public string PackageId { get; }
    public int ProgressBp { get; }
    public bool RobustAcrossFirstLegendAssignments { get; }

    public AdaptiveRouteLockCandidate(DamageLane lane, string goalUnitId, string packageId,
        int progressBp, bool robustAcrossFirstLegendAssignments)
    {
        if (lane == DamageLane.Unknown) throw new ArgumentException("A route lane must be known.", nameof(lane));
        if (string.IsNullOrWhiteSpace(goalUnitId)) throw new ArgumentException("A goal ID is required.", nameof(goalUnitId));
        if (string.IsNullOrWhiteSpace(packageId)) throw new ArgumentException("A package ID is required.", nameof(packageId));
        if (progressBp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(progressBp));
        Lane = lane;
        GoalUnitId = goalUnitId;
        PackageId = packageId;
        ProgressBp = progressBp;
        RobustAcrossFirstLegendAssignments = robustAcrossFirstLegendAssignments;
    }
}

public sealed record AdaptiveNavigationCandidate
{
    public string OptionId { get; }
    public bool IsRobust { get; }

    public AdaptiveNavigationCandidate(string optionId, bool isRobust)
    {
        if (string.IsNullOrWhiteSpace(optionId)) throw new ArgumentException("An option ID is required.", nameof(optionId));
        OptionId = optionId;
        IsRobust = isRobust;
    }
}

public sealed record AdaptiveBuildSnapshot
{
    public long MatchGeneration { get; }
    public int Round { get; }
    public int? ActiveStoryStage { get; }
    public bool FirstRareObserved { get; }
    public int? SpecialUncommonWispCount { get; }
    public int? RareWispCount { get; }
    public ImmutableArray<LegendBuildCandidate> LegendCandidates { get; }
    public ImmutableArray<string> NewlyObservedLegendIds { get; }
    public AdaptiveRouteLockCandidate? RouteCandidate { get; }
    public AdaptiveNavigationCandidate? NavigationCandidate { get; }
    public ManualLatches ManualLatches { get; }
    public bool IsTransient { get; }
    public bool IsConfirmedReset { get; }

    public AdaptiveBuildSnapshot(long matchGeneration, int round, int? activeStoryStage,
        bool firstRareObserved, int? specialUncommonWispCount, int? rareWispCount,
        ImmutableArray<LegendBuildCandidate> legendCandidates,
        ImmutableArray<string> newlyObservedLegendIds, AdaptiveRouteLockCandidate? routeCandidate,
        AdaptiveNavigationCandidate? navigationCandidate, ManualLatches manualLatches,
        bool isTransient, bool isConfirmedReset)
    {
        if (matchGeneration < 0) throw new ArgumentOutOfRangeException(nameof(matchGeneration));
        if (round is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(round));
        if (activeStoryStage is < 0 or > 14) throw new ArgumentOutOfRangeException(nameof(activeStoryStage));
        if (specialUncommonWispCount < 0) throw new ArgumentOutOfRangeException(nameof(specialUncommonWispCount));
        if (rareWispCount < 0) throw new ArgumentOutOfRangeException(nameof(rareWispCount));
        MatchGeneration = matchGeneration;
        Round = round;
        ActiveStoryStage = activeStoryStage;
        FirstRareObserved = firstRareObserved;
        SpecialUncommonWispCount = specialUncommonWispCount;
        RareWispCount = rareWispCount;
        LegendCandidates = legendCandidates.IsDefault ? ImmutableArray<LegendBuildCandidate>.Empty : legendCandidates;
        NewlyObservedLegendIds = newlyObservedLegendIds.IsDefault
            ? ImmutableArray<string>.Empty
            : newlyObservedLegendIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        RouteCandidate = routeCandidate;
        NavigationCandidate = navigationCandidate;
        ManualLatches = manualLatches;
        IsTransient = isTransient;
        IsConfirmedReset = isConfirmedReset;
    }
}

public sealed record AdaptiveBuildState
{
    public long MatchGeneration { get; init; }
    public PlannerPhase Phase { get; init; }
    public FirstRareHistory FirstRareHistory { get; init; }
    public FirstLegendHistory FirstLegendHistory { get; init; }
    public string? PendingLegendId { get; init; }
    public string? LockedFirstLegendId { get; init; }
    public ImmutableArray<string> PossibleFirstLegendIds { get; init; }
    public BasisPointInterval HistoryAbandonmentUncertaintyBp { get; init; }
    public bool MarinefordConfirmed { get; init; }
    public AdaptiveRouteLockCandidate? RouteLock { get; init; }
    public string? NavigationLockId { get; init; }
    public ManualLatches ManualLatches { get; init; }

    public static AdaptiveBuildState Initial(long matchGeneration)
    {
        if (matchGeneration < 0) throw new ArgumentOutOfRangeException(nameof(matchGeneration));
        return new AdaptiveBuildState
        {
            MatchGeneration = matchGeneration,
            Phase = PlannerPhase.AwaitFirstRare,
            FirstRareHistory = FirstRareHistory.NotObserved,
            FirstLegendHistory = FirstLegendHistory.NotObserved,
            PossibleFirstLegendIds = ImmutableArray<string>.Empty,
            ManualLatches = ManualLatches.None
        };
    }
}

public sealed record AdaptiveBuildTransition(
    AdaptiveBuildState State,
    string? SuggestedLegendId,
    string? ProvisionalNavigationId,
    ImmutableArray<AdaptiveBuildBlocker> Blockers);
