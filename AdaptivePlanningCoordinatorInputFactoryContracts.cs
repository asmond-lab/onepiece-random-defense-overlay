using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record AdaptivePlanningInputSource
{
    public required long MatchGeneration { get; init; }
    public required long RecognitionRevision { get; init; }
    public required int Round { get; init; }
    public required PlannerPhase Phase { get; init; }
    public int? ActiveStoryStage { get; init; }
    public ImmutableArray<string> CompletedStoryMilestones { get; init; } = [];
    public required IReadOnlyList<InventoryEntry> Inventory { get; init; }
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }
    public required string GoalUnitId { get; init; }
    public ImmutableArray<string> RouteGoalUnitIds { get; init; } = [];
    public ImmutableArray<string> PlannedGoalUnitIds { get; init; } = [];
    public required string NavigationOptionId { get; init; }
    public required GoroseiMode GoroseiMode { get; init; }
    public ImmutableArray<string> CompletedTopUnitIds { get; init; } = [];
    public ImmutableArray<string> GrowthUnitIds { get; init; } = [];
    public ImmutableDictionary<string, int> RewardWisps { get; init; } =
        ImmutableDictionary<string, int>.Empty;
    public ImmutableArray<string> PreviouslyObservedLegendIds { get; init; } = [];
    public ImmutableArray<PlanningValue> AdditionalValues { get; init; } = [];
    public required ManualLatches ManualLatches { get; init; }
    public RouteQuestSnapshot RouteQuests { get; init; } = RouteQuestSnapshot.Unknown;
    public bool PursueBothRouteQuests { get; init; }
    public bool IsTransient { get; init; }
    public string? CurrentOverlayRecommendationId { get; init; }
    public string? LockedOverlayRecommendationId { get; init; }
}
