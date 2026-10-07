using System.Collections.Immutable;

namespace OrandOverlay;

public sealed class AlliedForcesNavigationSimulation
{
    private readonly IAlliedRouteCounterfactualEvaluator evaluator;
    private readonly AlliedRouteCounterfactualEvaluation baseline;
    private readonly int minimumCraftPoints;
    private readonly int doubleWisps;
    private readonly int wildcards;
    private readonly int initialPoints;
    private readonly int initialChanges;
    private readonly int wispsPerConversion;
    private readonly int lumberPerConversion;
    private readonly int pointsPerConversion;

    public AlliedForcesNavigationSimulation(
        NavigationMechanicsProfile profile,
        IAlliedRouteCounterfactualEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(profile);
        this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        minimumCraftPoints = Parameter(profile, "allied-double-craft", "minimumUnitPointValue");
        doubleWisps = Parameter(profile, "allied-double-craft", "wispsPerCraft");
        wildcards = Parameter(profile, "emergency-wildcard-grant", "wildcards");
        initialPoints = Parameter(profile, "trait-resource-conversion", "initialPoints");
        initialChanges = Parameter(profile, "trait-resource-conversion", "initialChanges");
        wispsPerConversion = Parameter(profile, "trait-resource-conversion", "randomWispsPerUse");
        lumberPerConversion = Parameter(profile, "trait-resource-conversion", "lumberPerUse");
        pointsPerConversion = Parameter(profile, "trait-resource-conversion", "pointsPerUse");
        RequireProfileBindings(profile);
        baseline = Evaluate(AlliedRouteCounterfactualBundle.Empty);
    }

    public AlliedForcesSimulationResult SimulateDoubleBenefit(
        ImmutableArray<int> futureCraftPointValues)
    {
        if (futureCraftPointValues.IsDefault) throw new ArgumentException("Crafts must be initialized.", nameof(futureCraftPointValues));
        if (futureCraftPointValues.Any(value => value < 0)) throw new ArgumentOutOfRangeException(nameof(futureCraftPointValues));
        var qualifying = futureCraftPointValues.Count(value => value >= minimumCraftPoints);
        var bundle = new AlliedRouteCounterfactualBundle { RandomWisps = qualifying * doubleWisps };
        return Exact(AlliedForcesNavigationOption.DoubleBenefit, AlliedForcesUserTaxonomy.HighCeiling,
            AlliedForcesSimulationDisposition.Eligible, bundle, 0, 0, false);
    }

    public AlliedForcesSimulationResult SimulateEmergencyCall(
        ImmutableArray<AlliedSpecialLeaf> deficits,
        ImmutableArray<int> futureCraftPointValues)
    {
        ValidateCrafts(futureCraftPointValues);
        var normalized = NormalizeDeficits(deficits);
        if (normalized.Sum(value => value.MissingCount) < wildcards)
        {
            var empty = new AlliedRouteCounterfactualBundle();
            return Exact(AlliedForcesNavigationOption.EmergencyCall, AlliedForcesUserTaxonomy.FloorDefense,
                AlliedForcesSimulationDisposition.InsufficientWildcardDeficit, empty, 0, 0, false);
        }

        var forgone = futureCraftPointValues.Count(value => value >= minimumCraftPoints);
        AlliedRouteCounterfactualBundle? bestBundle = null;
        AlliedRouteCounterfactualEvaluation best = default;
        foreach (var allocation in EnumerateAllocations(normalized, wildcards))
        {
            var bundle = new AlliedRouteCounterfactualBundle
            {
                ForgoneRandomWisps = forgone,
                SpecialAllocations = allocation
            };
            var evaluation = Evaluate(bundle);
            if (bestBundle is null || Better(evaluation, best, 0, 0))
                (bestBundle, best) = (bundle, evaluation);
        }
        return FromEvaluations(AlliedForcesNavigationOption.EmergencyCall,
            AlliedForcesUserTaxonomy.FloorDefense, AlliedForcesSimulationDisposition.Eligible,
            bestBundle!, [(best, 0)], false);
    }

    public AlliedForcesSimulationResult SimulateTraitEngineering(
        AlliedResourceInterval randomWisps,
        AlliedResourceInterval lumber,
        int traitDeficit,
        int helperSelectionWispsLost,
        ImmutableArray<int> futureCraftPointValues)
    {
        if (traitDeficit < 0) throw new ArgumentOutOfRangeException(nameof(traitDeficit));
        if (helperSelectionWispsLost < 0) throw new ArgumentOutOfRangeException(nameof(helperSelectionWispsLost));
        ValidateCrafts(futureCraftPointValues);
        var futureWisps = futureCraftPointValues.Count(value => value >= minimumCraftPoints);
        var conversionDeficit = Math.Max(0, traitDeficit - initialPoints);
        var lowerCap = Math.Min(conversionDeficit,
            Math.Min(randomWisps.Lower / wispsPerConversion, lumber.Lower / lumberPerConversion));
        var upperCap = Math.Min(conversionDeficit,
            Math.Min(randomWisps.Upper / wispsPerConversion, lumber.Upper / lumberPerConversion));
        var disposition = traitDeficit == 0
            ? AlliedForcesSimulationDisposition.NoTraitDeficit
            : AlliedForcesSimulationDisposition.Eligible;
        var outcomes = new List<(AlliedRouteCounterfactualEvaluation Evaluation, int Conversions)>();
        AlliedRouteCounterfactualBundle? selected = null;
        AlliedRouteCounterfactualEvaluation selectedEvaluation = default;
        var selectedConversions = 0;
        for (var cap = lowerCap; cap <= upperCap; cap++)
        {
            AlliedRouteCounterfactualBundle? capBundle = null;
            AlliedRouteCounterfactualEvaluation capEvaluation = default;
            var capConversions = 0;
            for (var conversions = 0; conversions <= cap; conversions++)
            {
                var bundle = new AlliedRouteCounterfactualBundle
                {
                    RandomWisps = futureWisps,
                    RandomWispsSpent = conversions * wispsPerConversion,
                    LumberSpent = conversions * lumberPerConversion,
                    TraitPoints = initialPoints + conversions * pointsPerConversion,
                    TraitChanges = initialChanges,
                    HelperSelectionWispsLost = helperSelectionWispsLost
                };
                var evaluation = Evaluate(bundle);
                if (capBundle is null || Better(evaluation, capEvaluation, conversions, capConversions))
                    (capBundle, capEvaluation, capConversions) = (bundle, evaluation, conversions);
            }
            outcomes.Add((capEvaluation, capConversions));
            if (selected is null || Better(capEvaluation, selectedEvaluation, capConversions, selectedConversions))
                (selected, selectedEvaluation, selectedConversions) = (capBundle, capEvaluation, capConversions);
        }
        return FromEvaluations(AlliedForcesNavigationOption.TraitEngineering,
            AlliedForcesUserTaxonomy.ResourceOptimization, disposition, selected!, outcomes,
            !randomWisps.IsKnown || !lumber.IsKnown);
    }

    private AlliedForcesSimulationResult Exact(AlliedForcesNavigationOption option,
        AlliedForcesUserTaxonomy taxonomy, AlliedForcesSimulationDisposition disposition,
        AlliedRouteCounterfactualBundle bundle, int minimumConversions, int maximumConversions,
        bool unknown)
    {
        var evaluation = Evaluate(bundle);
        return FromEvaluations(option, taxonomy, disposition, bundle,
            [(evaluation, minimumConversions)], unknown, maximumConversions);
    }

    private AlliedForcesSimulationResult FromEvaluations(AlliedForcesNavigationOption option,
        AlliedForcesUserTaxonomy taxonomy, AlliedForcesSimulationDisposition disposition,
        AlliedRouteCounterfactualBundle bundle,
        IEnumerable<(AlliedRouteCounterfactualEvaluation Evaluation, int Conversions)> outcomes,
        bool unknown, int? exactMaximum = null)
    {
        var values = outcomes.ToArray();
        return new(option, disposition, taxonomy, bundle,
            Interval(values.Select(value => value.Evaluation.RouteBp - baseline.RouteBp)),
            Interval(values.Select(value => value.Evaluation.CoreBp)),
            Interval(values.Select(value => value.Evaluation.CaptureBp)),
            Interval(values.Select(value => value.Evaluation.ConflictBp)),
            values.Min(value => value.Conversions), exactMaximum ?? values.Max(value => value.Conversions), unknown);
    }

    private AlliedRouteCounterfactualEvaluation Evaluate(AlliedRouteCounterfactualBundle bundle)
    {
        bundle.Validate();
        return evaluator.Evaluate(bundle);
    }

    private static bool Better(AlliedRouteCounterfactualEvaluation candidate,
        AlliedRouteCounterfactualEvaluation incumbent, int candidateUses, int incumbentUses) =>
        candidate.RouteBp > incumbent.RouteBp ||
        candidate.RouteBp == incumbent.RouteBp && candidate.CoreBp > incumbent.CoreBp ||
        candidate.RouteBp == incumbent.RouteBp && candidate.CoreBp == incumbent.CoreBp &&
        (candidate.CaptureBp - candidate.ConflictBp > incumbent.CaptureBp - incumbent.ConflictBp ||
         candidate.CaptureBp - candidate.ConflictBp == incumbent.CaptureBp - incumbent.ConflictBp &&
         candidateUses < incumbentUses);

    private static AlliedBasisPointInterval Interval(IEnumerable<int> values) =>
        new(values.Min(), values.Max());

    private static int Parameter(NavigationMechanicsProfile profile, string transitionId, string name) =>
        profile.Transitions.Single(transition => transition.Id == transitionId).Integers
            .Single(parameter => parameter.Name == name).Value;

    private static void RequireProfileBindings(NavigationMechanicsProfile profile)
    {
        var expected = new[]
        {
            ("AlliedForces.DoubleBenefit", "allied-double-craft", NavigationTransitionKind.QualifyingCraftReward),
            ("AlliedForces.EmergencyCall", "emergency-wildcard-grant", NavigationTransitionKind.WildcardGrant),
            ("AlliedForces.TraitEngineering", "trait-resource-conversion", NavigationTransitionKind.ResourceConversion)
        };
        foreach (var item in expected)
        {
            _ = profile.Option(item.Item1);
            var transition = profile.Transitions.Single(value => value.Id == item.Item2);
            if (transition.OptionId != item.Item1 || transition.Kind != item.Item3)
                throw new InvalidDataException("Allied profile binding mismatch.");
        }
    }

    private static void ValidateCrafts(ImmutableArray<int> crafts)
    {
        if (crafts.IsDefault) throw new ArgumentException("Crafts must be initialized.", nameof(crafts));
        if (crafts.Any(value => value < 0)) throw new ArgumentOutOfRangeException(nameof(crafts));
    }

    private static ImmutableArray<AlliedSpecialLeaf> NormalizeDeficits(ImmutableArray<AlliedSpecialLeaf> deficits)
    {
        if (deficits.IsDefault) throw new ArgumentException("Deficits must be initialized.", nameof(deficits));
        return deficits.GroupBy(value => value.Id).Select(group =>
                new AlliedSpecialLeaf(group.Key, group.Sum(value => value.MissingCount)))
            .Where(value => value.MissingCount > 0).OrderBy(value => value.Id).ToImmutableArray();
    }

    private static IEnumerable<ImmutableArray<AlliedSpecialAllocation>> EnumerateAllocations(
        ImmutableArray<AlliedSpecialLeaf> deficits, int remaining, int index = 0,
        ImmutableArray<AlliedSpecialAllocation> current = default)
    {
        if (current.IsDefault) current = [];
        if (index == deficits.Length)
        {
            if (remaining == 0) yield return current;
            yield break;
        }
        var leaf = deficits[index];
        for (var count = 0; count <= Math.Min(leaf.MissingCount, remaining); count++)
        {
            var next = count == 0 ? current : current.Add(new AlliedSpecialAllocation(leaf.Id, count));
            foreach (var allocation in EnumerateAllocations(deficits, remaining - count, index + 1, next))
                yield return allocation;
        }
    }
}
