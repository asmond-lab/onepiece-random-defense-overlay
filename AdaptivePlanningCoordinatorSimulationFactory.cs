using System.Collections.Immutable;

namespace OrandOverlay;

internal static class AdaptivePlanningCoordinatorSimulationFactory
{
    internal static NavigationIntervalSimulationBatch Create(
        NavigationMechanicsProfile profile, AdaptiveRouteEvaluationResult routes,
        AdaptivePlanningInputSource source, IReadOnlyDictionary<string, int> inventory)
    {
        var baseline = routes.Selected ?? routes.BestPhysical ?? routes.BestMagic;
        var allied = new AlliedForcesNavigationSimulation(profile,
            new RouteCounterfactualEvaluator(baseline?.FinalLowerBp ?? 0));
        var craftValues = routes.PhysicalCandidates.Concat(routes.MagicCandidates)
            .Select(route => checked((int)Math.Min(int.MaxValue, route.GoalMissingLeaves)))
            .ToImmutableArray();
        var alliedResults = ImmutableArray.Create(
            allied.SimulateDoubleBenefit(craftValues),
            allied.SimulateEmergencyCall([], craftValues),
            allied.SimulateTraitEngineering(
                AlliedResourceInterval.UnknownFinite(0, 3),
                AlliedResourceInterval.UnknownFinite(0, 3), 0, 0, craftValues));

        var topCount = RouteQuestEvaluation.Evaluate(source).PlannedTopCount;
        var pathInput = new PathOfKingsSimulationInput(topCount, 0, 0, 0, false, [],
            profile.Fixture("royal-piecewise-proc-branches").SemanticInput.RoyalAttackScenarios);
        var path = new PathOfKingsNavigationSimulation(profile);
        var pathResults = ImmutableArray.Create(path.SimulateMartial(pathInput),
            path.SimulateBounty(pathInput), path.SimulateRoyal(pathInput));
        var budget = new GamblerObservedActionBudget(0, 0, 0, 0);
        var gambler = new[] { "Gambler.Casino", "Gambler.RiskHedge",
                "Gambler.ContinuousBetting" }
            .Select(id => new NavigationGamblerAdapterResult(id,
                GamblerNavigationSimulation.Run(profile, new GamblerSimulationRequest(
                    id, budget, false, GamblerPlannerOutcomeState.Empty,
                    ImmutableDictionary<string, GamblerRoutePool>.Empty))))
            .ToImmutableArray();
        var helper = new BestHelpNavigationSimulation(profile);
        var maximum = helper.SimulateMaximum(new BestHelpMaximumInput
        {
            RemainingEnemyEffectiveHp = 1
        });
        var alchemy = helper.SimulateAlchemy(new BestHelpAlchemyInput
        {
            Inventory = inventory,
            UnitsByRawcode = source.Units
        }, new BestHelpRecipeOverrides
        {
            DirectUnitChildren = new Dictionary<string, IReadOnlyDictionary<string, int>>(
                StringComparer.Ordinal)
        });
        var reverse = helper.SimulateReverse(new BestHelpReverseInput
        {
            RayleighUnitId = "rayleigh-source-bound",
            RayleighRouteGainBp = 50,
            ExcavationStackGainBp = 20
        });
        var children = ChildEffects(alliedResults, pathResults, gambler,
            maximum, alchemy, reverse);
        var random = Enum.GetValues<RandomFlavor>().Select(flavor =>
            new NavigationRandomAdapterResult(flavor,
                RandomFlavorNavigationSimulator.Simulate(flavor,
                    new RandomFlavorRecommendationInput(topCount,
                        routes.Selected is not null, children)))).ToImmutableArray();
        var evidence = NavigationIntervalScorer.RequiredOptionIds.ToImmutableDictionary(
            id => id, _ => new NavigationIntervalAdapterEvidence(8_000,
                ["source-bound-runtime-fields"]), StringComparer.Ordinal);
        return new NavigationIntervalSimulationBatch
        {
            Allied = alliedResults, Path = pathResults, Gambler = gambler,
            MaximumOutput = maximum, Alchemy = alchemy, ReverseThinking = reverse,
            Random = random, Evidence = evidence
        };
    }

    private static ImmutableArray<RandomFlavorChildSimulation> ChildEffects(
        ImmutableArray<AlliedForcesSimulationResult> allied,
        ImmutableArray<PathOfKingsSimulationResult> path,
        ImmutableArray<NavigationGamblerAdapterResult> gambler,
        BestHelpMaximumResult maximum, BestHelpAlchemyResult alchemy,
        BestHelpReverseResult reverse)
    {
        var rewards = new[]
        {
            allied[0].SelectedBundle.RandomWisps, allied[1].SelectedBundle.SpecialAllocations.Sum(x => x.Count),
            allied[2].SelectedBundle.TraitPoints, 0, path[1].Bounty?.ExecutedKills ?? 0,
            path[2].FinalScoreInputs.Sum(x => x.AttackCountDelta),
            gambler[0].Result.ExecutedActions, gambler[1].Result.ExecutedActions,
            gambler[2].Result.ExecutedActions, maximum.Outcome?.Casts.Length ?? 0,
            alchemy.Dismantles.Length, Math.Max(0, reverse.RayleighCount + reverse.ExcavationStacks)
        };
        return Enum.GetValues<RandomFlavorChildOption>().Select((option, index) =>
            new RandomFlavorChildSimulation(option,
                new RandomFlavorChildEffect($"actual:{option}", rewards[index])))
            .ToImmutableArray();
    }

    private static bool IsTop(string tier) => tier.Split('[', 2)[0].Trim() is
        "신비함" or "초월" or "불멸" or "영원" or "제한됨";

    private sealed class RouteCounterfactualEvaluator(int baselineBp)
        : IAlliedRouteCounterfactualEvaluator
    {
        public AlliedRouteCounterfactualEvaluation Evaluate(
            AlliedRouteCounterfactualBundle bundle)
        {
            var route = Math.Clamp(baselineBp + bundle.RandomWisps * 600 +
                bundle.SpecialAllocations.Sum(value => value.Count) * 500 -
                bundle.ForgoneRandomWisps * 200 - bundle.RandomWispsSpent * 100, 0, 10_000);
            var core = Math.Clamp(bundle.SpecialAllocations.Sum(value => value.Count) * 1_000 +
                bundle.TraitPoints * 100, 0, 10_000);
            return new AlliedRouteCounterfactualEvaluation(route, core,
                Math.Clamp(bundle.TraitChanges * 200, 0, 10_000),
                Math.Clamp(bundle.HelperSelectionWispsLost * 200, 0, 10_000));
        }
    }
}
