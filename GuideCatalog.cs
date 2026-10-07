namespace OrandOverlay;

public sealed record GuideOption(int Number, string Name, string GoalId);

public static class GuideCatalog
{
    public static IReadOnlyList<GuideOption> Options { get; } =
        [new(1, "1 · 더글라스 불릿", "rawcode:180h")];
    public static GuideOption? Find(int number) => Options.FirstOrDefault(option => option.Number == number);
}

public enum QueenConversionInput { Unknown, KeepKing, UserConfirmedMissionsComplete, UserConfirmedStoryTooSlow }

public enum BulletGuideStage
{
    FirstLegend, SecondLegend, AirFoundation, FourthLegendReward, BulletMaterials,
    BossSupport, ControlSupport, HoldRound50, CraftBullet, Operating, BruleePreparation, DestructionRace, OpeningRoleRecovery, QueenConversion, EarlyStunSupport, FastUniqueRare, RoundUnknown
}

public enum FastUniqueState { Unknown, CurrentRareOwned, RarePreviouslyObserved, CompletedVerified, Expired, TerminalOutcomeUnknown }

public sealed record BulletGuidePlan(BulletGuideStage Stage, string? TargetUnitId, bool OwnedBullet)
{
    public bool FirstLegendRewardHandObserved { get; init; }
    public bool AwaitingRewardHand { get; init; }
    public SelectionWispBatch? SelectionBatch { get; init; }
    public int PendingSelectionOutputs { get; init; }
    public FastUniqueState FastUnique { get; init; }
    public int SelectionWisps { get; init; }
    public long RareCommonDeficit { get; init; }
    public long RareOtherDeficit { get; init; }
    public QueenConversionInput QueenInput { get; init; }
    public bool QueenConversionConfirmed => QueenInput is QueenConversionInput.UserConfirmedMissionsComplete or
        QueenConversionInput.UserConfirmedStoryTooSlow;
    public IReadOnlyList<string> ProtectedUnitIds { get; init; } = [];
    public int KnownLegendLowerBound { get; init; }
    public int AirCount { get; init; }
    public int BossCount { get; init; }
    public bool BossException { get; init; }
    public bool ActiveHighGambleQuest { get; init; }
    public HighGambleObservation HighGamble { get; init; } = HighGambleObservation.Unknown;
    public bool PursueDestructionKing { get; init; }
    public int ComponentCount { get; init; }
    public string Difficulty { get; init; } = "unknown";
    public BulletGuideSupport? Support { get; init; }
    public int Round { get; init; }
    public string PlannedNavigation => BulletGuidePolicy.NavigationId;
    public string? ConfirmedNavigation { get; init; }
}
