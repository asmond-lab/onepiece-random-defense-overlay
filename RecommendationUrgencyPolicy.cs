namespace OrandOverlay;

public enum RecommendationUrgency
{
    None,
    BossSurvival,
    StoryDeadline
}

internal sealed record RecommendationUrgencyResult(
    IReadOnlyList<Recommendation> Recommendations,
    RecommendationUrgency Urgency,
    string? Reason);

/// <summary>
/// 맵의 공통 30라운드 보스와 35라운드 스토리 마감에 맞춰 모든 목표의 지원 추천을
/// 재정렬한다. 목표 자체와 이미 계산된 후보 집합은 보존하고 특정 유닛을 하드코딩하지 않는다.
/// </summary>
internal static class RecommendationUrgencyPolicy
{
    internal const int SurvivalRound = 30;
    internal const int StoryDeadlineStage = 13;
    internal const int StoryDeadlineRound = 35;

    public static RecommendationUrgencyResult Apply(
        DataCatalog catalog,
        UnitDefinition goal,
        IReadOnlyList<InventoryEntry> inventory,
        IReadOnlyList<Recommendation> recommendations,
        int round,
        int completedStoryStage,
        string? difficulty)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(recommendations);
        if (recommendations.Count <= 1 || round < SurvivalRound)
            return new RecommendationUrgencyResult(recommendations, RecommendationUrgency.None, null);

        var currentMetrics = Metrics(catalog, inventory);
        var strategy = GoalStrategyCalculator.StrategyProfileFor(goal) ??
                       new GoalStrategyProfile(0, 0);
        var bossUrgent = currentMetrics.BossControl < strategy.BossControlTarget ||
                         currentMetrics.BerserkBossControl < strategy.BerserkBossControlTarget;
        var storyUrgent = completedStoryStage < StoryDeadlineStage;
        if (!bossUrgent && !storyUrgent)
            return new RecommendationUrgencyResult(recommendations, RecommendationUrgency.None, null);

        var readiness = CombatReadinessCalculator.Calculate(catalog, goal, inventory, difficulty);
        var indexed = recommendations.Select((recommendation, index) => new
        {
            Recommendation = recommendation,
            Index = index,
            Metrics = GoalStrategyCalculator.StrategyMetricsFor(
                catalog.Unit(recommendation.Route.GoalUnitId))
        });
        var ordered = indexed
            .OrderBy(candidate => Priority(candidate.Recommendation, candidate.Metrics,
                goal.Id, readiness, bossUrgent, storyUrgent))
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Recommendation)
            .ToList();
        var urgency = bossUrgent
            ? RecommendationUrgency.BossSurvival
            : RecommendationUrgency.StoryDeadline;
        var reason = urgency == RecommendationUrgency.BossSurvival
            ? $"{SurvivalRound}라운드 보스 대응 지원 우선"
            : $"{StoryDeadlineRound}라운드 스토리 {StoryDeadlineStage}단계 마감 대응 우선";
        if (!ReferenceEquals(ordered[0], recommendations[0]) ||
            !ordered.Select(item => item.Route.Id)
                .SequenceEqual(recommendations.Select(item => item.Route.Id),
                    StringComparer.OrdinalIgnoreCase))
            ordered[0].Warnings.Add(reason);
        return new RecommendationUrgencyResult(ordered, urgency, reason);
    }

    private static int Priority(
        Recommendation recommendation,
        StrategyMetrics metrics,
        string goalId,
        CombatReadiness readiness,
        bool bossUrgent,
        bool storyUrgent)
    {
        if (recommendation.Route.GoalUnitId.Equals(goalId,
                StringComparison.OrdinalIgnoreCase)) return 0;
        if (bossUrgent && (metrics.BossControl > 0 || metrics.BerserkBossControl > 0)) return 1;
        if (ImprovesReadiness(metrics, readiness)) return 2;
        if (storyUrgent && (metrics.SingleDamage > 0 || metrics.FinisherDamage > 0)) return 3;
        return 4;
    }

    private static bool ImprovesReadiness(StrategyMetrics metrics, CombatReadiness readiness) =>
        readiness.MissingStun > 0 && metrics.Stun > 0 ||
        readiness.MissingSlow > 0 && metrics.Slow > 0 ||
        readiness.DamageType == ReadinessDamageType.Physical &&
        readiness.MissingArmorReduction > 0 && metrics.ArmorReduction > 0 ||
        readiness.DamageType == ReadinessDamageType.Magic &&
        readiness.MissingMagicArmorSource && metrics.MagicArmorReduction > 0;

    private static StrategyMetrics Metrics(
        DataCatalog catalog,
        IReadOnlyList<InventoryEntry> inventory)
    {
        var metrics = new StrategyMetrics();
        foreach (var entry in inventory.Where(entry => entry.Count > 0))
            metrics += GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(entry.UnitId)) *
                       entry.Count;
        return metrics;
    }
}
