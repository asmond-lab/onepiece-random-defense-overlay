using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationPipelineTests
{
    private readonly DataCatalog _catalog;
    private readonly RecommendationEngine _engine;

    public RecommendationPipelineTests()
    {
        _catalog = new DataCatalog();
        _catalog.Load(loadCarryPolicy: false);
        _engine = new RecommendationEngine(_catalog);
    }

    [Fact]
    public void SpentMarinefordRewardsProduceTopRecommendationsWithoutCommittedPhase()
    {
        var goal = TopGoal(magic: false);
        var story = new StoryRewardSequenceDecision(
            RecommendationSequenceStage.TopAndNavigation,
            StorySequenceAction.WaitForRound20,
            "어인섬 · 스토리 9",
            "희귀 보상 사용 완료",
            "현재 패 반영 완료",
            "상위 경로를 계산합니다.",
            "5 상위+항법 [현재]",
            null,
            null,
            0,
            true);

        var candidates = RecommendationPipeline.ComputeCandidates(Request(
            goal, RecommendationSurface.StoryLegend, story));
        var result = RecommendationPipeline.Finalize(
            candidates, _catalog, goal, [], new FirstRareTargetPolicy(),
            round: 19, completedStoryStage: 9, difficulty: "악몽");

        Assert.Equal(RecommendationSurface.TopAndNavigation, result.Surface);
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains(result.Recommendations,
            recommendation => recommendation.Route.GoalUnitId == goal.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopPipelineMatchesDirectEngineCandidatesForPhysicalAndMagicGoals(bool magic)
    {
        var goal = TopGoal(magic);
        var direct = _engine.RecommendNearestCrafts(
            goal.Id, [], navigationMode: "AlliedForces.DoubleBenefit",
            buildVariant: BuildVariants.AutoId, difficulty: "악몽");

        var candidates = RecommendationPipeline.ComputeCandidates(Request(
            goal, RecommendationSurface.TopAndNavigation));

        Assert.Equal(direct.Select(item => item.Route.GoalUnitId),
            candidates.Recommendations.Select(item => item.Route.GoalUnitId));
    }

    [Fact]
    public void FastRareFinalizationKeepsFiveCandidatesAndSkipsLateUrgency()
    {
        var goal = TopGoal(magic: false);
        var candidates = RecommendationPipeline.ComputeCandidates(Request(
            goal, RecommendationSurface.FastRare));

        var result = RecommendationPipeline.Finalize(
            candidates, _catalog, goal, [], new FirstRareTargetPolicy(),
            round: 30, completedStoryStage: 0, difficulty: "악몽");

        Assert.Equal(RecommendationSurface.FastRare, result.Surface);
        Assert.Equal(5, result.Recommendations.Count);
        Assert.Equal(RecommendationUrgency.None, result.Urgency.Urgency);
    }

    [Fact]
    public void EveryTopGradeGoalUsesTheSameNonEmptyPipelineContract()
    {
        var goals = _catalog.AllUnits
            .Where(unit => TopGradePolicy.IsTopGrade(unit.Tier))
            .ToArray();

        Assert.NotEmpty(goals);
        foreach (var goal in goals)
        {
            var candidates = RecommendationPipeline.ComputeCandidates(Request(
                goal, RecommendationSurface.TopAndNavigation));
            var result = RecommendationPipeline.Finalize(
                candidates, _catalog, goal, [], new FirstRareTargetPolicy(),
                round: 21, completedStoryStage: 9, difficulty: "악몽");

            Assert.NotEmpty(result.Recommendations);
            Assert.InRange(result.Recommendations.Count, 1, 8);
            Assert.Equal(result.Recommendations.Count,
                result.Recommendations.Select(item => item.Route.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    private RecommendationPipelineRequest Request(
        UnitDefinition goal,
        RecommendationSurface surface,
        StoryRewardSequenceDecision? story = null) => new()
    {
        Engine = _engine,
        Goal = goal,
        Inventory = [],
        InitialSurface = surface,
        StorySequence = story,
        NavigationMode = "AlliedForces.DoubleBenefit",
        Gorosei = GoroseiMode.None,
        BuildVariant = BuildVariants.AutoId,
        Difficulty = "악몽"
    };

    private UnitDefinition TopGoal(bool magic) => _catalog.AllUnits.First(unit =>
        TopGradePolicy.IsTopGrade(unit.Tier) &&
        GoalStrategyCalculator.IsMagicDamageTier(unit.Tier) == magic);
}
