using System.Collections.Immutable;

namespace OrandOverlay;

public enum AlliedForcesNavigationOption
{
    DoubleBenefit,
    EmergencyCall,
    TraitEngineering
}

public enum AlliedForcesSimulationDisposition
{
    Eligible,
    InsufficientWildcardDeficit,
    NoTraitDeficit
}

public enum AlliedForcesUserTaxonomy
{
    HighCeiling,
    FloorDefense,
    ResourceOptimization
}

public readonly record struct AlliedResourceInterval
{
    public int Lower { get; }
    public int Upper { get; }
    public bool IsKnown => Lower == Upper;

    public AlliedResourceInterval(int lower, int upper)
    {
        if (lower < 0) throw new ArgumentOutOfRangeException(nameof(lower));
        if (upper < lower || upper > 10_000) throw new ArgumentOutOfRangeException(nameof(upper));
        Lower = lower;
        Upper = upper;
    }

    public static AlliedResourceInterval Known(int value) => new(value, value);
    public static AlliedResourceInterval UnknownFinite(int lower, int upper) => new(lower, upper);
}

public readonly record struct AlliedSpecialLeaf
{
    public int Id { get; }
    public int MissingCount { get; }

    public AlliedSpecialLeaf(int id, int missingCount)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (missingCount < 0) throw new ArgumentOutOfRangeException(nameof(missingCount));
        Id = id;
        MissingCount = missingCount;
    }
}

public readonly record struct AlliedSpecialAllocation
{
    public int LeafId { get; }
    public int Count { get; }

    public AlliedSpecialAllocation(int leafId, int count)
    {
        if (leafId < 0) throw new ArgumentOutOfRangeException(nameof(leafId));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        LeafId = leafId;
        Count = count;
    }
}

public sealed record AlliedRouteCounterfactualBundle
{
    public static AlliedRouteCounterfactualBundle Empty { get; } = new();

    public int RandomWisps { get; init; }
    public int RandomWispsSpent { get; init; }
    public int ForgoneRandomWisps { get; init; }
    public int LumberSpent { get; init; }
    public int TraitPoints { get; init; }
    public int TraitChanges { get; init; }
    public int HelperSelectionWispsLost { get; init; }
    public ImmutableArray<AlliedSpecialAllocation> SpecialAllocations { get; init; } = [];

    public AlliedRouteCounterfactualBundle()
    {
    }

    public void Validate()
    {
        if (RandomWisps < 0 || RandomWispsSpent < 0 || ForgoneRandomWisps < 0 || LumberSpent < 0 ||
            TraitPoints < 0 || TraitChanges < 0 || HelperSelectionWispsLost < 0)
            throw new InvalidOperationException("Counterfactual quantities cannot be negative.");
        if (SpecialAllocations.IsDefault) throw new InvalidOperationException("Allocations must be initialized.");
    }
}

public readonly record struct AlliedRouteCounterfactualEvaluation
{
    public int RouteBp { get; }
    public int CoreBp { get; }
    public int CaptureBp { get; }
    public int ConflictBp { get; }

    public AlliedRouteCounterfactualEvaluation(int routeBp, int coreBp, int captureBp, int conflictBp)
    {
        if (routeBp is < 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(routeBp));
        if (coreBp is < 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(coreBp));
        if (captureBp is < 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(captureBp));
        if (conflictBp is < 0 or > 10_000) throw new ArgumentOutOfRangeException(nameof(conflictBp));
        RouteBp = routeBp;
        CoreBp = coreBp;
        CaptureBp = captureBp;
        ConflictBp = conflictBp;
    }
}

public interface IAlliedRouteCounterfactualEvaluator
{
    AlliedRouteCounterfactualEvaluation Evaluate(AlliedRouteCounterfactualBundle bundle);
}

public readonly record struct AlliedBasisPointInterval
{
    public int Lower { get; }
    public int Upper { get; }

    public AlliedBasisPointInterval(int lower, int upper)
    {
        if (lower is < -10_000 or > 10_000) throw new ArgumentOutOfRangeException(nameof(lower));
        if (upper is < -10_000 or > 10_000 || upper < lower) throw new ArgumentOutOfRangeException(nameof(upper));
        Lower = lower;
        Upper = upper;
    }
}

public sealed record AlliedForcesSimulationResult(
    AlliedForcesNavigationOption Option,
    AlliedForcesSimulationDisposition Disposition,
    AlliedForcesUserTaxonomy Taxonomy,
    AlliedRouteCounterfactualBundle SelectedBundle,
    AlliedBasisPointInterval RouteDeltaBp,
    AlliedBasisPointInterval CoreBp,
    AlliedBasisPointInterval CaptureBp,
    AlliedBasisPointInterval ConflictBp,
    int MinimumConversions,
    int MaximumConversions,
    bool HasUnknownResources);
