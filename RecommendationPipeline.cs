namespace OrandOverlay;

internal sealed record RecommendationPipelineRequest
{
    public PlayMode Mode { get; init; } = PlayMode.Beginner;
    public BulletGuidePlan? GuidePlan { get; init; }
    public required RecommendationEngine Engine { get; init; }
    public required UnitDefinition Goal { get; init; }
    public required IReadOnlyList<InventoryEntry> Inventory { get; init; }
    public required RecommendationSurface InitialSurface { get; init; }
    public StoryRewardSequenceDecision? StorySequence { get; init; }
    public required string NavigationMode { get; init; }
    public NativeNavigationSnapshot NativeNavigation { get; init; } = NativeNavigationSnapshot.Unknown;
    public required GoroseiMode Gorosei { get; init; }
    public required string BuildVariant { get; init; }
    public required string Difficulty { get; init; }
    public int Round { get; init; }
    public int CompletedStoryStage { get; init; }
    public bool SuppressSeraphim { get; init; }
    public bool PrioritizeTargetRare { get; init; }
    public bool SuppressFirstRareShip { get; init; }
    public int CandidateCount { get; init; } = 8;
    public ManualGoalPlan? ManualPlan { get; init; }
    public string? CommittedCraftUnitId { get; init; }
}

internal sealed record RecommendationPipelineCandidates(
    RecommendationSurface Surface,
    IReadOnlyList<Recommendation> Recommendations,
    StoryRewardSequenceDecision? StorySequence);

internal sealed record RecommendationPipelineResult(
    RecommendationSurface Surface,
    IReadOnlyList<Recommendation> Recommendations,
    StoryRewardSequenceDecision? StorySequence,
    RecommendationUrgencyResult Urgency);

/// <summary>
/// 생산 오버레이와 시뮬레이터가 같은 surface 선택, 후보 생성, 첫 희귀 고정,
/// 전역 긴급도 후처리를 사용하도록 묶는 단일 추천 파이프라인이다.
/// </summary>
internal static class RecommendationPipeline
{
    public static RecommendationPipelineCandidates ComputeCandidates(
        RecommendationPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CandidateCount < 1)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.NativeNavigation.Status == NativeNavigationStatus.Conflict)
            return new(request.InitialSurface, [], null);
        request = request with { NavigationMode = request.NativeNavigation.Resolve(request.NavigationMode) ?? "Unselected" };
        if (request.Mode == PlayMode.Guide)
            return new RecommendationPipelineCandidates(RecommendationSurface.TopAndNavigation,
                request.GuidePlan?.TargetUnitId is { } guideTarget
                    ? request.Engine.RecommendGuideCraft(guideTarget, request.Inventory, request.GuidePlan,
                        request.Round, request.CompletedStoryStage) : [], request.GuidePlan is null ? null : request.StorySequence);

        if (request.ManualPlan is { } plan && plan.ActiveGoalId != request.Goal.Id)
            throw new ArgumentException("The active goal must match the manual plan.", nameof(request));
        // An observed selected top has already passed the opening recipe sequence.
        // Missing/late story recognition must not replace its support plan with a first legend.
        var ownedGoal = TopGradePolicy.IsTopGrade(request.Goal.Tier) && request.Inventory.Any(entry =>
            entry.Count > 0 && entry.UnitId.Equals(request.Goal.Id, StringComparison.OrdinalIgnoreCase));
        var surface = request.ManualPlan is not null || ownedGoal ? RecommendationSurface.TopAndNavigation
            : request.StorySequence is null
            ? request.InitialSurface
            : RecommendationSequencePolicy.Surface(request.StorySequence);
        IReadOnlyList<Recommendation> recommendations = surface switch
        {
            RecommendationSurface.FastRare =>
                request.Engine.RecommendFastRares(request.Inventory, 500),
            RecommendationSurface.StoryLegend
                when request.StorySequence?.RecommendedLegendId is { Length: > 0 } legendId =>
                request.Engine.RecommendNearestCrafts(
                    legendId,
                    request.Inventory,
                    request.CandidateCount,
                    request.NavigationMode,
                    request.Gorosei,
                    request.BuildVariant,
                    request.SuppressSeraphim,
                    difficulty: request.Difficulty,
                    round: request.Round,
                    completedStoryStage: request.CompletedStoryStage),
            RecommendationSurface.StoryLegend => [],
            _ => request.Engine.RecommendNearestCrafts(
                request.Goal.Id,
                request.Inventory,
                request.CandidateCount,
                request.NavigationMode,
                request.Gorosei,
                request.BuildVariant,
                request.SuppressSeraphim,
                request.PrioritizeTargetRare,
                request.SuppressFirstRareShip,
                suppressSecondaryTopCandidates: request.ManualPlan is not null,
                difficulty: request.Difficulty,
                round: request.Round,
                completedStoryStage: request.CompletedStoryStage,
                committedCraftUnitId: request.CommittedCraftUnitId)
        };
        if (request.ManualPlan is { } manual)
        {
            recommendations = request.Engine.Recascade(recommendations.Where(item =>
                    !TopGradePolicy.IsTopGrade(item.CompositionUnits.FirstOrDefault()?.Tier ?? "") ||
                    manual.CanCraftNewTop && manual.GoalIds.Contains(item.Route.GoalUnitId)).ToList(),
                manual.RecipeInventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }),
                null);
        }
        return new RecommendationPipelineCandidates(
            surface, recommendations, request.ManualPlan is null && !ownedGoal ? request.StorySequence : null);
    }

    public static RecommendationPipelineResult Finalize(
        RecommendationPipelineCandidates candidates,
        DataCatalog catalog,
        UnitDefinition selectedGoal,
        IReadOnlyList<InventoryEntry> inventory,
        FirstRareTargetPolicy firstRareTargets,
        int round,
        int completedStoryStage,
        string difficulty,
        int recommendationCount = 5)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selectedGoal);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(firstRareTargets);
        if (recommendationCount < 1)
            throw new ArgumentOutOfRangeException(nameof(recommendationCount));

        var recommendations = firstRareTargets.Apply(
            candidates.Surface, candidates.Recommendations, recommendationCount);
        var urgencyGoal = candidates.StorySequence?.RecommendedLegendId is
            { Length: > 0 } legendId
            ? catalog.Unit(legendId)
            : selectedGoal;
        var urgency = candidates.Surface == RecommendationSurface.FastRare
            ? new RecommendationUrgencyResult(
                recommendations, RecommendationUrgency.None, null)
            : RecommendationUrgencyPolicy.Apply(
                catalog,
                urgencyGoal,
                inventory,
                recommendations,
                round,
                completedStoryStage,
                difficulty);
        return new RecommendationPipelineResult(
            candidates.Surface,
            urgency.Recommendations,
            candidates.StorySequence,
            urgency);
    }
}
