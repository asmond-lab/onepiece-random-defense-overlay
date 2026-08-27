using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NavigationIntervalScorerAdapterTests
{
    [Fact]
    public void ConcreteOutputsAssembleAllOptionsAndPreserveProbabilitySupport()
    {
        var batch = Batch();
        var options = NavigationIntervalSimulationAdapter.Adapt(batch, new Projector());

        Assert.Equal(NavigationIntervalScorer.RequiredOptionIds, options.Select(x => x.OptionId));
        Assert.Equal(Enumerable.Range(0, 15), options.Select(x => x.OrdinalId));
        Assert.False(options[3].TopCompatible);
        Assert.Contains("path:HardIneligible", options[3].SourceReasonIds);
        Assert.Equal(batch.Path[1].Bounty!.TerminalStates.Select(x => x.Probability),
            options[4].CoupledScenarios.Single().Outcomes.Select(x => x.Probability));
        Assert.Equal(batch.Gambler[0].Result.Outcomes.Select(x => x.Mass),
            options[6].CoupledScenarios.Single().Outcomes.Select(x => x.Probability));
        Assert.Equal(12, options[12].CoupledScenarios.Single().Outcomes.Length);
        Assert.All(options[12].CoupledScenarios.Single().Outcomes,
            outcome => Assert.Equal(new Rational(1, 12), outcome.Probability));

        var scored = NavigationIntervalScorer.Score(new NavigationIntervalScoringRequest
        {
            Round = 21, BeforeBuildBp = 1_000, BeforeCoreBp = 10_000,
            BeforeCombatBp = 1_000, RouteConfidenceBp = 10_000,
            MechanicsConfidenceBp = 10_000, Options = options
        });
        Assert.Equal(NavigationRecommendationState.Actionable, scored.State);
        Assert.Equal("Random.YellowFlavor", scored.RecommendedOptionId);
        Assert.True(scored.IsRecommendationOnly);
        Assert.False(scored.ClaimsRuntimeSelection);
    }

    [Fact]
    public void ConcreteUncertaintyAndScenarioGateCapConfidenceAtMinimum()
    {
        var batch = Batch() with
        {
            Allied = Batch().Allied.SetItem(2, Allied(
                AlliedForcesNavigationOption.TraitEngineering, unknown: true)),
            MaximumOutput = Batch().MaximumOutput with
            {
                Disposition = BestHelpSimulationDisposition.ScenarioGated,
                UnknownSpellIds = ["A0ZZ"]
            }
        };
        var options = NavigationIntervalSimulationAdapter.Adapt(batch, new Projector());

        Assert.Equal(8_000, options[2].ConfidenceFactorsBp.Min());
        Assert.Contains("allied:Eligible", options[2].SourceReasonIds);
        Assert.Equal(8_000, options[9].ConfidenceFactorsBp.Min());
        Assert.Contains("best-help:unknown:A0ZZ", options[9].SourceReasonIds);
    }

    private static NavigationIntervalSimulationBatch Batch()
    {
        var pathCategory = new PathOfKingsCategoryOutcome(new Rational(0, 1), 0, 0, 1);
        var bounty = new PathBountyResult(2, new Rational(1, 1), new Rational(1, 2),
            [new(0, new Rational(1, 2)), new(1, new Rational(1, 2))], [],
            PathSignedRational.Zero);
        var final = new PathOfKingsFinalScoreInput(PathOfKingsOption.RoyalLoader,
            new Rational(0, 1), 0, 0, 1, new Rational(1, 1), 1,
            new Rational(0, 1), new Rational(0, 1), PathSignedRational.Zero);
        var path = ImmutableArray.Create(
            new PathOfKingsSimulationResult(PathOfKingsOption.MartialLaw,
                PathOfKingsDisposition.HardIneligible, pathCategory, null, [], []),
            new PathOfKingsSimulationResult(PathOfKingsOption.BountyHunter,
                PathOfKingsDisposition.Eligible, pathCategory, bounty, [], [final with
                { Option = PathOfKingsOption.BountyHunter }]),
            new PathOfKingsSimulationResult(PathOfKingsOption.RoyalLoader,
                PathOfKingsDisposition.Eligible, pathCategory, null,
                [new PathRoyalScenarioResult(null!, final)], [final]));
        var gambler = new[] { "Gambler.Casino", "Gambler.RiskHedge",
            "Gambler.ContinuousBetting" }.Select((id, index) =>
                new NavigationGamblerAdapterResult(id, Gambler(index))).ToImmutableArray();
        var random = Enum.GetValues<RandomFlavor>().Select(flavor =>
            new NavigationRandomAdapterResult(flavor, Random(flavor))).ToImmutableArray();
        var evidence = NavigationIntervalScorer.RequiredOptionIds.ToImmutableDictionary(
            id => id, _ => new NavigationIntervalAdapterEvidence(10_000, []),
            StringComparer.Ordinal);
        return new NavigationIntervalSimulationBatch
        {
            Allied = [Allied(AlliedForcesNavigationOption.DoubleBenefit),
                Allied(AlliedForcesNavigationOption.EmergencyCall),
                Allied(AlliedForcesNavigationOption.TraitEngineering)],
            Path = path, Gambler = gambler, MaximumOutput = Maximum(),
            Alchemy = new BestHelpAlchemyResult
            {
                Disposition = BestHelpSimulationDisposition.Confirmed, Dismantles = [],
                RouteLeavesGained = ImmutableDictionary<string, int>.Empty, ManaSpent = 0
            },
            ReverseThinking = new BestHelpReverseResult
            {
                Disposition = BestHelpSimulationDisposition.Confirmed,
                RayleighUnitId = "rayleigh", RayleighCount = 1, ExcavationStacks = 1,
                LostHelperActionIds = [], HelperLossBp = 0, NetRouteGainBp = 100,
                UnknownSpellIds = []
            },
            Random = random, Evidence = evidence
        };
    }

    private static AlliedForcesSimulationResult Allied(
        AlliedForcesNavigationOption option, bool unknown = false) => new(option,
        AlliedForcesSimulationDisposition.Eligible,
        option == AlliedForcesNavigationOption.DoubleBenefit
            ? AlliedForcesUserTaxonomy.HighCeiling : AlliedForcesUserTaxonomy.FloorDefense,
        AlliedRouteCounterfactualBundle.Empty, new AlliedBasisPointInterval(0, 0),
        new AlliedBasisPointInterval(5_000, 5_000),
        new AlliedBasisPointInterval(0, 0), new AlliedBasisPointInterval(0, 0),
        1, unknown ? 2 : 1, unknown);

    private static GamblerSimulationResult Gambler(int index)
    {
        var state = new GamblerPlannerOutcomeState(
            [new GamblerRewardQuantity($"reward-{index}", 1)], 1, 0, 0);
        return new GamblerSimulationResult(ArithmeticDisposition.Allowed,
            WaveDisposition.SafeRecommendation, null, 1, 10, 1, 16, [],
            [new GamblerWeightedPlannerState(state, new Rational(1, 1))],
            new Rational(1, 1), []);
    }

    private static BestHelpMaximumResult Maximum() => new()
    {
        Disposition = BestHelpSimulationDisposition.Confirmed, UnknownSpellIds = [],
        IncrementalDamage = 1, IncrementalControlTargetSeconds = 1,
        DamageGainBp = 100, ControlGainBp = 100, CombatBp = 100
    };

    private static RandomFlavorSimulationResult Random(RandomFlavor flavor) => new(
        RandomFlavorEligibility.Eligible,
        Enum.GetValues<RandomFlavorChildOption>().Select(option =>
            new RandomFlavorBranchOutcome(option, new Rational(1, 12), true, false, true,
                false, new RandomFlavorChildEffect($"{flavor}:{option}", 1),
                new RandomFlavorBonus(RandomFlavorBonusKind.RandomWisp, 1))).ToImmutableArray());

    private sealed class Projector : INavigationIntervalOutcomeProjector
    {
        public NavigationIntervalProjectedState Project(AlliedForcesSimulationResult result,
            NavigationIntervalBound bound) => State((int)result.Option);
        public NavigationIntervalProjectedState Project(NavigationGamblerAdapterResult result,
            GamblerPlannerOutcomeState state) => State(6 + Array.IndexOf(
                new[] { "Gambler.Casino", "Gambler.RiskHedge", "Gambler.ContinuousBetting" },
                result.OptionId));
        public NavigationIntervalProjectedState Project(PathOfKingsSimulationResult result,
            PathBountyTerminalState state) => State(4);
        public NavigationIntervalProjectedState Project(PathOfKingsFinalScoreInput input) =>
            State(input.Option == PathOfKingsOption.RoyalLoader ? 5 : 4);
        public NavigationIntervalProjectedState Project(BestHelpMaximumResult result,
            NavigationIntervalBound bound) => State(9);
        public NavigationIntervalProjectedState Project(BestHelpAlchemyResult result) => State(10);
        public NavigationIntervalProjectedState Project(BestHelpReverseResult result,
            NavigationIntervalBound bound) => State(11);
        public NavigationIntervalProjectedState Project(NavigationRandomAdapterResult result,
            RandomFlavorBranchOutcome outcome) => State(12 + (int)result.Flavor);
        private static NavigationIntervalProjectedState State(int ordinal) =>
            new(2_000 + ordinal * 500, 10_000, 2_000 + ordinal * 500);
    }
}
