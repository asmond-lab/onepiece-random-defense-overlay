using System.Collections.Immutable;

namespace OrandOverlay;

public static class AdaptiveBuildStateMachine
{
    public static AdaptiveBuildTransition Advance(AdaptiveBuildState state, AdaptiveBuildSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.IsConfirmedReset)
            return Result(AdaptiveBuildState.Initial(snapshot.MatchGeneration));
        if (snapshot.IsTransient)
            return Result(state, blockers: [AdaptiveBuildBlocker.TransientSnapshot]);
        if (snapshot.MatchGeneration != state.MatchGeneration)
            return Result(state, blockers: [AdaptiveBuildBlocker.MatchGenerationMismatch]);

        state = state with
        {
            ManualLatches = new ManualLatches(
                state.ManualLatches.GoalOverride || snapshot.ManualLatches.GoalOverride,
                state.ManualLatches.NavigationOverride || snapshot.ManualLatches.NavigationOverride)
        };

        string? suggestedLegend = null;
        string? provisionalNavigation = null;
        var blockers = ImmutableArray.CreateBuilder<AdaptiveBuildBlocker>();
        var continueTransitions = true;
        while (continueTransitions)
        {
            continueTransitions = false;
            switch (state.Phase)
            {
                case PlannerPhase.AwaitFirstRare:
                    if (snapshot.FirstRareObserved)
                    {
                        state = state with
                        {
                            FirstRareHistory = FirstRareHistory.Observed,
                            Phase = PlannerPhase.AccumulateSpecialUncommon
                        };
                        continueTransitions = true;
                    }
                    else if (snapshot.ActiveStoryStage >= 7)
                    {
                        state = state with
                        {
                            FirstRareHistory = FirstRareHistory.Unknown,
                            FirstLegendHistory = FirstLegendHistory.Unknown,
                            HistoryAbandonmentUncertaintyBp = new BasisPointInterval(0, 2500),
                            Phase = PlannerPhase.AwaitMarineford
                        };
                        continueTransitions = true;
                    }
                    else blockers.Add(AdaptiveBuildBlocker.AwaitingFirstRare);
                    break;

                case PlannerPhase.AccumulateSpecialUncommon:
                    if (snapshot.NewlyObservedLegendIds.Length > 0)
                    {
                        state = ObserveFirstLegends(state, snapshot.NewlyObservedLegendIds) with
                        {
                            PendingLegendId = null,
                            Phase = PlannerPhase.AwaitMarineford
                        };
                        continueTransitions = true;
                        break;
                    }
                    if (snapshot.ActiveStoryStage is null)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnknownStoryStage);
                        break;
                    }
                    if (snapshot.ActiveStoryStage < 6)
                    {
                        if (snapshot.SpecialUncommonWispCount is null)
                            blockers.Add(AdaptiveBuildBlocker.UnknownSpecialUncommonWisps);
                        else if (snapshot.SpecialUncommonWispCount > 0)
                            blockers.Add(AdaptiveBuildBlocker.UnspentSpecialUncommonWisps);
                        break;
                    }
                    if (snapshot.SpecialUncommonWispCount is null)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnknownSpecialUncommonWisps);
                        break;
                    }
                    if (snapshot.SpecialUncommonWispCount > 0)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnspentSpecialUncommonWisps);
                        break;
                    }

                    var candidate = snapshot.ActiveStoryStage >= 7
                        ? SelectFallback(snapshot.LegendCandidates)
                        : SelectComplete(snapshot.LegendCandidates);
                    if (candidate is null)
                    {
                        if (snapshot.LegendCandidates.Any(item => item.Resources == ResourceCompletion.Unknown))
                            blockers.Add(AdaptiveBuildBlocker.UnknownLegendResources);
                        else blockers.Add(AdaptiveBuildBlocker.NoLegendCandidate);
                        break;
                    }
                    if (snapshot.ActiveStoryStage >= 7)
                        blockers.Add(AdaptiveBuildBlocker.LegendFallback);
                    suggestedLegend = candidate.UnitId;
                    state = state with { PendingLegendId = candidate.UnitId, Phase = PlannerPhase.ChooseLegend };
                    break;

                case PlannerPhase.ChooseLegend:
                    suggestedLegend = state.PendingLegendId;
                    if (snapshot.NewlyObservedLegendIds.Length == 0)
                    {
                        blockers.Add(AdaptiveBuildBlocker.AwaitingLegendOutput);
                        break;
                    }
                    state = ObserveFirstLegends(state, snapshot.NewlyObservedLegendIds) with
                    {
                        PendingLegendId = null,
                        Phase = PlannerPhase.AwaitMarineford
                    };
                    continueTransitions = true;
                    break;

                case PlannerPhase.AwaitMarineford:
                    if (snapshot.ActiveStoryStage is null)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnknownStoryStage);
                        break;
                    }
                    if (snapshot.ActiveStoryStage < 9)
                    {
                        blockers.Add(AdaptiveBuildBlocker.AwaitingMarineford);
                        break;
                    }
                    state = state with { MarinefordConfirmed = true, Phase = PlannerPhase.SpendRares };
                    continueTransitions = true;
                    break;

                case PlannerPhase.SpendRares:
                    if (snapshot.RareWispCount is null)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnknownRareWisps);
                        break;
                    }
                    if (snapshot.RareWispCount > 0)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnspentRareWisps);
                        break;
                    }
                    state = state with { Phase = PlannerPhase.CommitRound20 };
                    continueTransitions = true;
                    break;

                case PlannerPhase.CommitRound20:
                    if (snapshot.Round < 20)
                    {
                        blockers.Add(AdaptiveBuildBlocker.AwaitingRound20);
                        break;
                    }
                    if (state.ManualLatches.GoalOverride)
                    {
                        blockers.Add(AdaptiveBuildBlocker.ManualGoalOverride);
                        break;
                    }
                    if (snapshot.RouteCandidate is null)
                    {
                        blockers.Add(AdaptiveBuildBlocker.NoRouteCandidate);
                        break;
                    }
                    if (state.FirstLegendHistory is FirstLegendHistory.Unknown or FirstLegendHistory.Ambiguous &&
                        !snapshot.RouteCandidate.RobustAcrossFirstLegendAssignments)
                    {
                        blockers.Add(AdaptiveBuildBlocker.UnknownFirstLegendHistory);
                        blockers.Add(AdaptiveBuildBlocker.RouteNotRobust);
                        break;
                    }
                    state = state with { RouteLock = snapshot.RouteCandidate, Phase = PlannerPhase.Committed };
                    continueTransitions = true;
                    break;

                case PlannerPhase.Committed:
                    state = RecascadeLockedProgress(state, snapshot.RouteCandidate);
                    if (state.NavigationLockId is not null) break;
                    if (state.ManualLatches.NavigationOverride)
                    {
                        blockers.Add(AdaptiveBuildBlocker.ManualNavigationOverride);
                        break;
                    }
                    if (snapshot.NavigationCandidate is null) break;
                    if (snapshot.Round == 20)
                    {
                        provisionalNavigation = snapshot.NavigationCandidate.OptionId;
                        break;
                    }
                    if (snapshot.Round is < 21 or > 23) break;
                    if (!snapshot.NavigationCandidate.IsRobust)
                    {
                        blockers.Add(AdaptiveBuildBlocker.NavigationNotRobust);
                        break;
                    }
                    state = state with { NavigationLockId = snapshot.NavigationCandidate.OptionId };
                    break;

                case PlannerPhase.ManualOverride:
                    throw new InvalidOperationException("Manual latches are represented independently from planner phase.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state.Phase, "Unknown planner phase.");
            }
        }

        return Result(state, suggestedLegend, provisionalNavigation, blockers.ToImmutable());
    }

    private static LegendBuildCandidate? SelectComplete(ImmutableArray<LegendBuildCandidate> candidates) =>
        candidates.Where(candidate => candidate.CardAllocationComplete && candidate.Resources == ResourceCompletion.Complete)
            .OrderByDescending(candidate => candidate.PreservedRouteCount)
            .ThenBy(candidate => candidate.UnitId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static LegendBuildCandidate? SelectFallback(ImmutableArray<LegendBuildCandidate> candidates) =>
        candidates.OrderBy(candidate => candidate.MissingLeaves)
            .ThenByDescending(candidate => candidate.PreservedRouteCount)
            .ThenBy(candidate => candidate.UnitId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static AdaptiveBuildState ObserveFirstLegends(AdaptiveBuildState state, ImmutableArray<string> ids)
    {
        if (ids.Length == 1)
            return state with
            {
                FirstLegendHistory = FirstLegendHistory.Observed,
                LockedFirstLegendId = ids[0],
                PossibleFirstLegendIds = ids
            };
        return state with
        {
            FirstLegendHistory = FirstLegendHistory.Ambiguous,
            LockedFirstLegendId = null,
            PossibleFirstLegendIds = ids,
            HistoryAbandonmentUncertaintyBp = new BasisPointInterval(0, 2500)
        };
    }

    private static AdaptiveBuildState RecascadeLockedProgress(AdaptiveBuildState state,
        AdaptiveRouteLockCandidate? candidate)
    {
        if (state.RouteLock is null || candidate is null ||
            !state.RouteLock.GoalUnitId.Equals(candidate.GoalUnitId, StringComparison.Ordinal) ||
            !state.RouteLock.PackageId.Equals(candidate.PackageId, StringComparison.Ordinal))
            return state;
        return state with
        {
            RouteLock = new AdaptiveRouteLockCandidate(state.RouteLock.Lane,
                state.RouteLock.GoalUnitId, state.RouteLock.PackageId, candidate.ProgressBp,
                state.RouteLock.RobustAcrossFirstLegendAssignments)
        };
    }

    private static AdaptiveBuildTransition Result(AdaptiveBuildState state, string? suggestedLegend = null,
        string? provisionalNavigation = null, ImmutableArray<AdaptiveBuildBlocker> blockers = default) =>
        new(state, suggestedLegend, provisionalNavigation,
            blockers.IsDefault ? ImmutableArray<AdaptiveBuildBlocker>.Empty : blockers);
}
