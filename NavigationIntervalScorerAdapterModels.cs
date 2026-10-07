using System.Collections.Immutable;

namespace OrandOverlay;

public enum NavigationIntervalBound { Lower, Upper }

public readonly record struct NavigationIntervalProjectedState(
    int BuildBp, int CoreBp, int CombatBp);

public sealed record NavigationGamblerAdapterResult(
    string OptionId, GamblerSimulationResult Result);

public sealed record NavigationRandomAdapterResult(
    RandomFlavor Flavor, RandomFlavorSimulationResult Result);

public sealed record NavigationIntervalAdapterEvidence(
    int ConfidenceBp, ImmutableArray<string> MissingSignalIds);

public interface INavigationIntervalOutcomeProjector
{
    NavigationIntervalProjectedState Project(
        AlliedForcesSimulationResult result, NavigationIntervalBound bound);
    NavigationIntervalProjectedState Project(
        NavigationGamblerAdapterResult result, GamblerPlannerOutcomeState state);
    NavigationIntervalProjectedState Project(
        PathOfKingsSimulationResult result, PathBountyTerminalState state);
    NavigationIntervalProjectedState Project(PathOfKingsFinalScoreInput input);
    NavigationIntervalProjectedState Project(
        BestHelpMaximumResult result, NavigationIntervalBound bound);
    NavigationIntervalProjectedState Project(BestHelpAlchemyResult result);
    NavigationIntervalProjectedState Project(
        BestHelpReverseResult result, NavigationIntervalBound bound);
    NavigationIntervalProjectedState Project(
        NavigationRandomAdapterResult result, RandomFlavorBranchOutcome outcome);
}

public sealed record NavigationIntervalSimulationBatch
{
    public required ImmutableArray<AlliedForcesSimulationResult> Allied { get; init; }
    public required ImmutableArray<PathOfKingsSimulationResult> Path { get; init; }
    public required ImmutableArray<NavigationGamblerAdapterResult> Gambler { get; init; }
    public required BestHelpMaximumResult MaximumOutput { get; init; }
    public required BestHelpAlchemyResult Alchemy { get; init; }
    public required BestHelpReverseResult ReverseThinking { get; init; }
    public required ImmutableArray<NavigationRandomAdapterResult> Random { get; init; }
    public required ImmutableDictionary<string, NavigationIntervalAdapterEvidence> Evidence
        { get; init; }
}
