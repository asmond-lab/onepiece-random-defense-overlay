using System.Collections.Immutable;

namespace OrandOverlay;

public enum CoachActionKind
{
    Waiting, Recognition, Craft, Gather, Story, Reward, Navigation,
    Economy, Upgrade, Maintain, Finished, Item
}

public sealed record CoachDecision(
    CoachActionKind Kind,
    string Id,
    string Title,
    string Controls,
    string Reason,
    string Confirmation,
    string Milestone)
{
    public string? TargetUnitId { get; init; }
    public string? ConsumedUnitId { get; init; }
    public string? NavigationOptionId { get; init; }
    public int? MilestoneRound { get; init; }
    public string PreservedMaterials { get; init; } = "";
    public ImmutableDictionary<string, long> PreservedMaterialCounts { get; init; } =
        ImmutableDictionary<string, long>.Empty;
    public string UnknownSignals { get; init; } = "";
    public string Alternative { get; init; } = "";
    public string OperationGuide { get; init; } = "";
    public string GoalLabel { get; init; } = "";
    public string ChangeReason { get; init; } = "";
    public string CompletionNotice { get; init; } = "";
    public double? MaterialCompletion { get; init; }
    public RecipeCraftStep? CraftRecipe { get; init; }
    public ObservedCraftProgress? CraftProgress { get; init; }
    public IReadOnlyList<RecipeCraftStep> RecipePreview { get; init; } = [];
    public bool RequiresUserConfirmation { get; init; }
    public bool RequiresNavigationChoice { get; init; }
    public bool IsUrgent { get; init; }
    public bool CraftDeferredForReward { get; init; }
    public bool ShowBountyHunterPreparationTip { get; init; }
    public string? RewardWispId { get; init; }
    public SelectionWispBatch? SelectionBatch { get; init; }
    public int? SelectionWispsAfterBatch { get; init; }
    public string Constraint { get; init; } = "";
}

// Counts cover additional recipe outputs in this plan, excluding bodies owned at its start.
public sealed record ObservedCraftProgress(string UnitId, int RequiredCount, int CompletedCount, bool AwaitingRecognition)
{
    public int RemainingCount => Math.Max(0, RequiredCount - CompletedCount);
}

// Remaining quantities are observed acquisition deficits, never a native reward receipt.
public sealed record SelectionWispItem(string UnitId, int RemainingCount);
public sealed record SelectionWispBatch(string TargetUnitId, ImmutableArray<SelectionWispItem> Items)
{
    public int RemainingCount => Items.Sum(item => item.RemainingCount);
}

public sealed record CoachFrame
{
    public PlayMode Mode { get; init; } = PlayMode.Beginner;
    public int GuideNumber { get; init; }
    public BulletGuidePlan? GuidePlan { get; init; }
    public BulletGuideRuntimeState GuideRuntime { get; init; } = BulletGuideRuntimeState.Unknown;
    public HelperUnitState? HelperState { get; init; }
    public ImmutableArray<CombatUnitState> CombatObservations { get; init; } = [];
    public GoroseiObservation Gorosei { get; init; } = GoroseiObservation.Unknown;
    public GoroseiPlanningSelection UserGoroseiPlan { get; init; } = GoroseiPlanningSelection.Unknown;
    public GoroseiPlanningSelection PlanningGorosei => GoroseiPlanningSelection.Resolve(
        Gorosei, MatchGeneration, RecognitionRevision, UserGoroseiPlan);
    public GoroseiMode CurrentGoroseiEffect => IsCurrent && MatchGeneration > 0 && RecognitionRevision > 0 &&
        Round >= 50 && Gorosei.MatchGeneration == MatchGeneration && Gorosei.RecognitionRevision == RecognitionRevision &&
        (!Gorosei.IsSynthetic || Gorosei.EffectObservationRound == Round)
            ? Gorosei.EffectMode : GoroseiMode.None;
    public long RecognitionRevision { get; init; }
    public ShipReservations? ShipReservations { get; init; }
    public int? LoadedClearCount { get; init; }
    public ImmutableArray<string> SelectedGoalIds { get; init; } = [];
    public string ManualGoalSummary { get; init; } = "";
    public string NavigationConstraint { get; init; } = "";
    public required long MatchGeneration { get; init; }
    public required long Revision { get; init; }
    public required int Round { get; init; }
    public required int CompletedStoryStage { get; init; }
    public required bool IsCurrent { get; init; }
    public bool Paused { get; init; }
    public bool GuideVisible { get; init; } = true;
    public required ImmutableDictionary<string, int> Inventory { get; init; }
    public ImmutableDictionary<string, long?> Signals { get; init; } =
        ImmutableDictionary<string, long?>.Empty;
    public string Difficulty { get; init; } = "unknown";
    public bool HasKnownDifficulty => MatchOutcomeDetector.IsKnownDifficulty(Difficulty);
    public string DifficultyLabel => HasKnownDifficulty ? Difficulty : "난이도 미확인";
    public int? ClearRound => HasKnownDifficulty ? MatchOutcomeDetector.ClearRound(Difficulty) : null;
    public string Outcome { get; init; } = "unknown";
    public NativeNavigationSnapshot NativeNavigation { get; init; } = NativeNavigationSnapshot.Unknown;
    public string? ConfirmedNavigation { get; init; }
    public string? SuggestedNavigation { get; init; }
    public string? GoalId { get; init; }
    public string? CommittedCraftUnitId { get; init; }
    public StoryRewardSequenceDecision? Story { get; init; }
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = [];
    public IReadOnlyList<AutoCombineStep> CraftSteps { get; init; } = [];
    public IReadOnlyList<RareRerollAdvice> Rerolls { get; init; } = [];
    public IReadOnlyList<SpecialDismantleAdvice> Dismantles { get; init; } = [];
    public IReadOnlyList<GreenBloodAdvice> GreenBlood { get; init; } = [];
    public bool GreenBloodAvailable { get; init; }
    public IReadOnlyList<EmergencySummonAdvice> Wisps { get; init; } = [];
    public ImmutableDictionary<string, int> RewardWisps { get; init; } =
        ImmutableDictionary<string, int>.Empty;
}
