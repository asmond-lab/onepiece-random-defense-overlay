namespace OrandOverlay;

public sealed class AdaptivePlanningCoordinatorOutcomeProjector(
    int beforeBuildBp, int beforeCoreBp, int beforeCombatBp)
    : INavigationIntervalOutcomeProjector
{
    public NavigationIntervalProjectedState Project(AlliedForcesSimulationResult result,
        NavigationIntervalBound bound)
    {
        var route = bound == NavigationIntervalBound.Lower
            ? result.RouteDeltaBp.Lower : result.RouteDeltaBp.Upper;
        var core = bound == NavigationIntervalBound.Lower
            ? result.CoreBp.Lower : result.CoreBp.Upper;
        var capture = bound == NavigationIntervalBound.Lower
            ? result.CaptureBp.Lower - result.ConflictBp.Upper
            : result.CaptureBp.Upper - result.ConflictBp.Lower;
        return State(route, core, capture);
    }

    public NavigationIntervalProjectedState Project(NavigationGamblerAdapterResult result,
        GamblerPlannerOutcomeState state)
    {
        var reward = state.Rewards.Sum(value => value.Count);
        return State(reward * 90, state.RerollsAvailable * 40,
            state.GambleActions * 25);
    }

    public NavigationIntervalProjectedState Project(PathOfKingsSimulationResult result,
        PathBountyTerminalState state) => State(
            result.Category.TotalBossLumber * 30, 0, -state.DryStreak * 10);

    public NavigationIntervalProjectedState Project(PathOfKingsFinalScoreInput input) =>
        State(RationalBp(input.RouteDelta), input.BossLumber * 30,
            input.AttackCountDelta * 20 + input.TargetsWithin400 * 10);

    public NavigationIntervalProjectedState Project(BestHelpMaximumResult result,
        NavigationIntervalBound bound) => State(0,
            result.Outcome?.CoreDeficitsClosed * 100 ?? 0, result.CombatBp);

    public NavigationIntervalProjectedState Project(BestHelpAlchemyResult result) =>
        State(result.RouteLeavesGained.Values.Sum() * 120, 0, 0);

    public NavigationIntervalProjectedState Project(BestHelpReverseResult result,
        NavigationIntervalBound bound) => State(result.NetRouteGainBp, 0,
            -result.HelperLossBp);

    public NavigationIntervalProjectedState Project(NavigationRandomAdapterResult result,
        RandomFlavorBranchOutcome outcome)
    {
        var bonus = outcome.Bonus?.Count ?? 0;
        return State(outcome.RewardUnits * 70 + bonus * 30, 0, bonus * 20);
    }

    private NavigationIntervalProjectedState State(int buildDelta, int coreDelta,
        int combatDelta) => new(Clamp(beforeBuildBp + buildDelta),
            Clamp(beforeCoreBp + coreDelta), Clamp(beforeCombatBp + combatDelta));

    private static int RationalBp(PathSignedRational value)
    {
        if (value.Denominator == 0) return 0;
        var scaled = value.Numerator * 10_000 / value.Denominator;
        if (scaled < -10_000) return -10_000;
        if (scaled > 10_000) return 10_000;
        return (int)scaled;
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 10_000);
}
