using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class FirstRareTargetPolicyTests
{
    [Fact]
    public void ProgressedVanderDeckenStaysFirstWhenSentomaruTemporarilyRanksHigher()
    {
        var policy = new FirstRareTargetPolicy();
        var vander = Recommendation("vander", 14, 15);
        var sentomaru = Recommendation("sentomaru", 10, 15);
        Assert.Equal("vander", policy.Apply(RecommendationSurface.FastRare,
            [vander, sentomaru])[0].Route.GoalUnitId);

        var rerankedVander = Recommendation("vander", 14, 15);
        var rerankedSentomaru = Recommendation("sentomaru", 15, 15);
        var result = policy.Apply(RecommendationSurface.FastRare,
            [rerankedSentomaru, rerankedVander]);

        Assert.Equal("vander", result[0].Route.GoalUnitId);
    }

    [Fact]
    public void ManualSelectionTakesOverAndZeroProgressDoesNotFreezeEmptyHandChoice()
    {
        var policy = new FirstRareTargetPolicy();
        var vanderEmpty = Recommendation("vander", 0, 15);
        var sentomaruEmpty = Recommendation("sentomaru", 0, 15);
        policy.Apply(RecommendationSurface.FastRare, [vanderEmpty, sentomaruEmpty]);

        var sentomaruProgressed = Recommendation("sentomaru", 3, 15);
        var vanderStillEmpty = Recommendation("vander", 0, 15);
        var progressed = policy.Apply(RecommendationSurface.FastRare,
            [sentomaruProgressed, vanderStillEmpty]);
        Assert.Equal("sentomaru", progressed[0].Route.GoalUnitId);

        policy.Select("craft:vander", [vanderStillEmpty, sentomaruProgressed]);
        var manuallySelected = policy.Apply(RecommendationSurface.FastRare,
            [sentomaruProgressed, vanderStillEmpty]);
        Assert.Equal("vander", manuallySelected[0].Route.GoalUnitId);

        var later = policy.Apply(RecommendationSurface.TopAndNavigation,
            [sentomaruProgressed, vanderStillEmpty]);
        Assert.Equal("sentomaru", later[0].Route.GoalUnitId);
    }

    [Fact]
    public void ResetClearsProgressedFirstRareTarget()
    {
        var policy = new FirstRareTargetPolicy();
        var vander = Recommendation("vander", 14, 15);
        var sentomaru = Recommendation("sentomaru", 15, 15);
        policy.Apply(RecommendationSurface.FastRare, [vander, sentomaru]);

        policy.Reset();
        var result = policy.Apply(RecommendationSurface.FastRare, [sentomaru, vander]);

        Assert.Equal("sentomaru", result[0].Route.GoalUnitId);
    }

    private static Recommendation Recommendation(string id, long owned, long required) =>
        new()
        {
            Route = new RouteDefinition
            {
                Id = "craft:" + id,
                GoalUnitId = id,
                Name = id
            },
            RecipeProgress = new RecipeProgress
            {
                OwnedLeafCount = owned,
                RequiredLeafCount = required
            },
            CompositionUnits =
            [
                new CompositionUnitDetail
                {
                    UnitId = id,
                    Name = id,
                    Tier = "희귀함"
                }
            ]
        };
}
