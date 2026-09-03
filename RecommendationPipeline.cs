namespace OrandOverlay;

internal sealed record RecommendationPipelineRequest
{
    public required RecommendationEngine Engine { get; init; }
    public required UnitDefinition Goal { get; init; }
    public required IReadOnlyList<InventoryEntry> Inventory { get; init; }
    public required RecommendationSurface InitialSurface { get; init; }
    public StoryRewardSequenceDecision? StorySequence { get; init; }
    public required string NavigationMode { get; init; }
    public required GoroseiMode Gorosei { get; init; }
    public required string BuildVariant { get; init; }
    public required string Difficulty { get; init; }
    public bool SuppressSeraphim { get; init; }
    public bool PrioritizeTargetRare { get; init; }
    public bool SuppressFirstRareShip { get; init; }
    public int CandidateCount { get; init; } = 8;
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

        var surface = request.StorySequence is null
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
                    difficulty: request.Difficulty),
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
                difficulty: request.Difficulty)
        };
        return new RecommendationPipelineCandidates(
            surface, recommendations, request.StorySequence);
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
