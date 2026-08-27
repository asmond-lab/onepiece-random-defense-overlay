using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AlliedForcesNavigationSimulationTests
{
    [Fact]
    [Trait("Task11", "ExactBundle")]
    public void DoubleBenefitGrantsTwoWispsForEachQualifyingFutureCraft()
    {
        var evaluator = new RecordingEvaluator(BundleScore);
        var simulation = Create(evaluator);

        var result = simulation.SimulateDoubleBenefit([100, 101, 250]);

        Assert.Equal(AlliedForcesNavigationOption.DoubleBenefit, result.Option);
        Assert.Equal(AlliedForcesUserTaxonomy.HighCeiling, result.Taxonomy);
        Assert.Equal(4, result.SelectedBundle.RandomWisps);
        Assert.Equal(new AlliedBasisPointInterval(120, 120), result.RouteDeltaBp);
        Assert.Equal(new AlliedBasisPointInterval(52, 52), result.CaptureBp);
        Assert.Equal(new AlliedBasisPointInterval(8, 8), result.ConflictBp);
    }

    [Fact]
    [Trait("Task11", "WildcardJointAllocation")]
    public void EmergencyCallExhaustsJointMultiplicityAndPricesLostAlliedBase()
    {
        var evaluator = new RecordingEvaluator(bundle =>
        {
            var wildcardValue = bundle.SpecialAllocations.Sum(value => value.LeafId switch
            {
                0 => value.Count * 1_500,
                1 => value.Count * 3_000,
                _ => value.Count * 1_000
            });
            return Evaluation(
                4_000 + wildcardValue - bundle.ForgoneRandomWisps * 40,
                4_000 + wildcardValue,
                wildcardValue / 2,
                bundle.ForgoneRandomWisps * 25);
        });
        var simulation = Create(evaluator);

        var result = simulation.SimulateEmergencyCall(
            [new(0, 3), new(1, 2), new(2, 1)], [100, 101, 500]);

        Assert.Equal(AlliedForcesSimulationDisposition.Eligible, result.Disposition);
        Assert.Equal(AlliedForcesUserTaxonomy.FloorDefense, result.Taxonomy);
        Assert.Equal(3, result.SelectedBundle.SpecialAllocations.Sum(value => value.Count));
        Assert.Equal([new AlliedSpecialAllocation(0, 1), new AlliedSpecialAllocation(1, 2)],
            result.SelectedBundle.SpecialAllocations.ToArray());
        Assert.Equal(2, result.SelectedBundle.ForgoneRandomWisps);
        Assert.Equal(10_000, result.CoreBp.Lower);
        Assert.Equal(6, evaluator.Bundles.Count(bundle => bundle.SpecialAllocations.Length > 0));
    }

    [Fact]
    [Trait("Task11", "Gate")]
    public void EmergencyCallRejectsFewerThanExactlyThreeUsefulWildcardUses()
    {
        var simulation = Create(new RecordingEvaluator(BundleScore));

        var result = simulation.SimulateEmergencyCall([new(3, 2), new(8, 0)], [101]);

        Assert.Equal(AlliedForcesSimulationDisposition.InsufficientWildcardDeficit, result.Disposition);
        Assert.Empty(result.SelectedBundle.SpecialAllocations);
        Assert.Equal(0, result.SelectedBundle.ForgoneRandomWisps);
    }

    [Fact]
    [Trait("Task11", "TraitOptimization")]
    public void TraitEngineeringIncludesImmediateFutureAndLossBundlesWithoutForcingConversion()
    {
        var evaluator = new RecordingEvaluator(bundle => Evaluation(
            4_000 + bundle.TraitPoints * 100 + bundle.TraitChanges * 20 +
            bundle.RandomWisps * 30 - bundle.RandomWispsSpent * 200 -
            bundle.LumberSpent * 100 - bundle.HelperSelectionWispsLost * 50,
            3_000 + bundle.TraitPoints * 100,
            100 + bundle.RandomWisps * 10,
            bundle.HelperSelectionWispsLost * 50));
        var simulation = Create(evaluator);

        var result = simulation.SimulateTraitEngineering(
            AlliedResourceInterval.Known(10), AlliedResourceInterval.Known(5),
            traitDeficit: 4, helperSelectionWispsLost: 2, [101, 100, 700]);

        Assert.Equal(AlliedForcesSimulationDisposition.Eligible, result.Disposition);
        Assert.Equal(AlliedForcesUserTaxonomy.ResourceOptimization, result.Taxonomy);
        Assert.Equal(1, result.SelectedBundle.TraitPoints);
        Assert.Equal(1, result.SelectedBundle.TraitChanges);
        Assert.Equal(2, result.SelectedBundle.RandomWisps);
        Assert.Equal(0, result.SelectedBundle.RandomWispsSpent);
        Assert.Equal(0, result.SelectedBundle.LumberSpent);
        Assert.Equal(2, result.SelectedBundle.HelperSelectionWispsLost);
        Assert.Equal(0, result.MaximumConversions);
    }

    [Fact]
    [Trait("Task11", "UnknownInterval")]
    public void TraitEngineeringKeepsUnknownResourcesAsFiniteOptimizedInterval()
    {
        var evaluator = new RecordingEvaluator(bundle => Evaluation(
            4_000 + bundle.TraitPoints * 500 - bundle.RandomWispsSpent * 100 -
            bundle.LumberSpent * 100,
            2_000 + bundle.TraitPoints * 500,
            bundle.TraitPoints * 100,
            bundle.LumberSpent * 25));
        var simulation = Create(evaluator);

        var result = simulation.SimulateTraitEngineering(
            AlliedResourceInterval.UnknownFinite(0, 4),
            AlliedResourceInterval.UnknownFinite(0, 2),
            traitDeficit: 3, helperSelectionWispsLost: 0, []);

        Assert.True(result.HasUnknownResources);
        Assert.Equal(new AlliedBasisPointInterval(500, 900), result.RouteDeltaBp);
        Assert.Equal(new AlliedBasisPointInterval(2_500, 3_500), result.CoreBp);
        Assert.Equal(0, result.MinimumConversions);
        Assert.Equal(2, result.MaximumConversions);
        Assert.Equal(4, result.SelectedBundle.RandomWispsSpent);
        Assert.Equal(2, result.SelectedBundle.LumberSpent);
    }

    [Fact]
    [Trait("Task11", "NoTraitDeficit")]
    public void NoTraitDeficitNeverConvertsAndCanRemainNonPositive()
    {
        var evaluator = new RecordingEvaluator(bundle => Evaluation(
            4_000 - bundle.HelperSelectionWispsLost * 100 - bundle.RandomWispsSpent * 100,
            4_000, 0, bundle.HelperSelectionWispsLost * 100));
        var simulation = Create(evaluator);

        var result = simulation.SimulateTraitEngineering(
            AlliedResourceInterval.Known(100), AlliedResourceInterval.Known(100),
            traitDeficit: 0, helperSelectionWispsLost: 1, []);

        Assert.Equal(AlliedForcesSimulationDisposition.NoTraitDeficit, result.Disposition);
        Assert.Equal(0, result.SelectedBundle.RandomWispsSpent);
        Assert.Equal(0, result.SelectedBundle.LumberSpent);
        Assert.Equal(0, result.MaximumConversions);
        Assert.True(result.RouteDeltaBp.Upper <= 0);
    }

    private static AlliedForcesNavigationSimulation Create(RecordingEvaluator evaluator) =>
        new(NavigationMechanicsProfileLoader.LoadFromDirectory(
            Path.Combine(RepositoryRoot(), "Data")), evaluator);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Data", "navigation-mechanics-2314.json")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static AlliedRouteCounterfactualEvaluation BundleScore(AlliedRouteCounterfactualBundle bundle) =>
        Evaluation(4_000 + bundle.RandomWisps * 30, 3_000 + bundle.RandomWisps * 20,
            40 + bundle.RandomWisps * 3, bundle.RandomWisps * 2);

    private static AlliedRouteCounterfactualEvaluation Evaluation(
        int route, int core, int capture, int conflict) => new(
            Math.Clamp(route, 0, 10_000), Math.Clamp(core, 0, 10_000),
            Math.Clamp(capture, 0, 10_000), Math.Clamp(conflict, 0, 10_000));

    private sealed class RecordingEvaluator(
        Func<AlliedRouteCounterfactualBundle, AlliedRouteCounterfactualEvaluation> evaluate)
        : IAlliedRouteCounterfactualEvaluator
    {
        public List<AlliedRouteCounterfactualBundle> Bundles { get; } = [];

        public AlliedRouteCounterfactualEvaluation Evaluate(AlliedRouteCounterfactualBundle bundle)
        {
            Bundles.Add(bundle);
            return evaluate(bundle);
        }
    }
}
