using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class SecondaryTopGateTests
{
    [Fact]
    public void NoSecondaryCandidatesStillRespectTake()
    {
        var result = SecondaryTopGate.Apply(
            Enumerable.Range(0, 10)
                .Select(index => Recommendation($"support-{index}",
                    100 - index))
                .ToList(),
            "goal", GoalCarryMode.Unknown, Readiness(false), take: 4,
            _ => false);

        Assert.Equal(4, result.Recommendations.Count);
    }

    [Fact]
    public void RealEngineCannotRecommendSecondTopBeforeReadiness()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        const string goalId = "rawcode:A90H";

        var recommendations = new RecommendationEngine(catalog)
            .RecommendNearestCrafts(goalId, [], take: 12,
                navigationMode: "AlliedForces.EmergencyCall");
        var secondaryTops = recommendations
            .Where(item => item.Route.GoalUnitId != goalId &&
                           TopGradePolicy.IsTopGrade(
                               catalog.Unit(item.Route.GoalUnitId).Tier))
            .ToList();

        Assert.Empty(secondaryTops);
        Assert.True(recommendations.Count <= 12);
        Assert.Contains(recommendations,
            item => item.DeferredSecondaryTopCount > 0 &&
                    item.DeferredSecondaryTopReason is not null);
    }

    [Fact]
    public void UnreadyBuildRemovesEverySecondaryTop()
    {
        var result = SecondaryTopGate.Apply(
            [Recommendation("goal", 100), Recommendation("support", 90),
                Recommendation("top-a", 80), Recommendation("top-b", 70)],
            "goal",
            GoalCarryMode.MultiAllowed,
            Readiness(isReady: false),
            take: 4,
            id => id.StartsWith("top", StringComparison.Ordinal) || id == "goal");

        Assert.Equal(["goal", "support"],
            result.Recommendations.Select(item => item.Route.GoalUnitId));
        Assert.Equal(2, result.DeferredCount);
        Assert.False(string.IsNullOrWhiteSpace(result.DeferredReason));
    }

    [Fact]
    public void RequiredPartnerIsNotTreatedAsOptionalSecondaryTop()
    {
        var result = SecondaryTopGate.Apply(
            [Recommendation("goal", 100), Recommendation("partner", 90),
                Recommendation("top-a", 80)],
            "goal",
            GoalCarryMode.Unknown,
            Readiness(false),
            take: 4,
            id => id is "goal" or "partner" or "top-a",
            new HashSet<string>(["partner"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["goal", "partner"],
            result.Recommendations.Select(item => item.Route.GoalUnitId));
        Assert.Equal(1, result.DeferredCount);
    }


    [Theory]
    [InlineData(GoalCarryMode.Unknown)]
    [InlineData(GoalCarryMode.SoloPreferred)]
    public void SafeSingleTopModesDoNotOpenSecondaryAfterReadiness(
        GoalCarryMode mode)
    {
        var result = SecondaryTopGate.Apply(
            [Recommendation("goal", 100), Recommendation("top-a", 90)],
            "goal", mode, Readiness(true), 4,
            id => id is "goal" or "top-a");

        Assert.Single(result.Recommendations);
        Assert.Equal("goal", result.Recommendations[0].Route.GoalUnitId);
    }

    [Fact]
    public void MultiRequiredAddsOneStableSecondaryAfterSupports()
    {
        var source = new[]
        {
            Recommendation("goal", 100),
            Recommendation("support", 95),
            Recommendation("top-z", 80, 0.7),
            Recommendation("top-a", 80, 0.7)
        };

        var orders = Enumerable.Range(0, 20)
            .Select(_ => SecondaryTopGate.Apply(source, "goal",
                GoalCarryMode.MultiRequired, Readiness(true), 4,
                id => id == "goal" || id.StartsWith("top",
                    StringComparison.Ordinal)))
            .Select(result => string.Join(",",
                result.Recommendations.Select(item => item.Route.GoalUnitId)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["goal,support,top-a"], orders);
    }

    [Fact]
    public void FullSupportSlotsDoNotOverflowTakeForMultiRequired()
    {
        var result = SecondaryTopGate.Apply(
            [Recommendation("goal", 100), Recommendation("support-1", 90),
                Recommendation("support-2", 80), Recommendation("top-a", 70)],
            "goal", GoalCarryMode.MultiRequired, Readiness(true), take: 3,
            id => id is "goal" or "top-a");

        Assert.Equal(3, result.Recommendations.Count);
        Assert.DoesNotContain(result.Recommendations,
            item => item.Route.GoalUnitId == "top-a");
        Assert.Equal(1, result.DeferredCount);
    }

    private static CombatReadiness Readiness(bool isReady) =>
        isReady
            ? new CombatReadiness(ReadinessDamageType.Physical,
                1.4, 1.4, 102, 102, 211, 211, 0, 0)
            : new CombatReadiness(ReadinessDamageType.Physical,
                1.3, 1.4, 90, 102, 180, 211, 0, 0);

    private static Recommendation Recommendation(
        string id, double score, double completion = 0) =>
        new()
        {
            Route = new RouteDefinition
            {
                Id = "route:" + id,
                GoalUnitId = id,
                Name = id
            },
            Score = score,
            RecipeProgress = new RecipeProgress
            {
                RequiredLeafCount = 100,
                OwnedLeafCount = (long)Math.Round(completion * 100)
            }
        };
}
