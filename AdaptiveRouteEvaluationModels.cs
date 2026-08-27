using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record AdaptiveRouteCandidate
{
    public required string GoalUnitId { get; init; }
    public required string VariantId { get; init; }
    public ImmutableArray<string> ExplicitRequiredRootIds { get; init; } = [];
    public ImmutableArray<string> StrategySupportPoolIds { get; init; } = [];
    public ImmutableArray<string> OptionalAffinityRootIds { get; init; } = [];
    public int SampleCount { get; init; }
    public int RecognitionConfidenceBp { get; init; } = 10000;
    public int DamageConfidenceBp { get; init; } = 10000;
    public int RecipeConfidenceBp { get; init; } = 10000;
    public bool HasHardBlocker { get; init; }
}

public sealed record AdaptiveRouteEvaluationInput
{
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }
    public required IReadOnlyDictionary<string, int> Inventory { get; init; }
    public required ImmutableArray<AdaptiveRouteCandidate> Candidates { get; init; }
    public ImmutableArray<string> FirstObservedLegendIds { get; init; } = [];
    public bool IsAutomatic { get; init; } = true;
    public ClearBuildStats? ClearStats { get; init; }
}

public sealed record AdaptiveRouteScore
{
    public required DamageLane Lane { get; init; }
    public required string GoalUnitId { get; init; }
    public required string VariantId { get; init; }
    public required ImmutableArray<string> PackageUnitIds { get; init; }
    public required ImmutableArray<string> SynthesizedSupportUnitIds { get; init; }
    public required IReadOnlyDictionary<string, long> ConsumedInventory { get; init; }
    public required int GoalBp { get; init; }
    public required int RecipePackageBp { get; init; }
    public required int StrategyPackageBp { get; init; }
    public required int PackageBp { get; init; }
    public required int AffinityBp { get; init; }
    public required int BaseBp { get; init; }
    public required int OpportunityPenaltyBp { get; init; }
    public required int AbandonmentLowerBp { get; init; }
    public required int AbandonmentUpperBp { get; init; }
    public required int FinalLowerBp { get; init; }
    public required int FinalUpperBp { get; init; }
    public required int ConfidenceBp { get; init; }
    public required int EvidenceConfidenceBp { get; init; }
    public required int SampleCount { get; init; }
    public required long GoalRequiredLeaves { get; init; }
    public required long PackageRequiredLeaves { get; init; }
    public required long GoalMissingLeaves { get; init; }
    public required long PackageMissingLeaves { get; init; }
    public required bool HasHardBlocker { get; init; }
}

public sealed record AdaptiveRouteEvaluationResult
{
    public required ImmutableArray<AdaptiveRouteScore> PhysicalCandidates { get; init; }
    public required ImmutableArray<AdaptiveRouteScore> MagicCandidates { get; init; }
    public AdaptiveRouteScore? BestPhysical { get; init; }
    public AdaptiveRouteScore? BestMagic { get; init; }
    public AdaptiveRouteScore? Selected { get; init; }
}
