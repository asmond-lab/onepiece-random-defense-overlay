using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GoalProgressionTests
{
    [Fact]
    public void JinbeKeepsFirstRareRecommendationUntilTargetRareIsObserved()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var engine = new RecommendationEngine(catalog);
        const string goalId = "rawcode:A90H";
        var targetRares = engine.RecipeRareUnitIds(goalId);
        var gate = new FirstRareRecommendationGate();

        var prioritize = gate.ShouldPrioritize(
            goalId, [], targetRares, currentRound: 45);
        var recommendations = engine.RecommendNearestCrafts(
            goalId, [], navigationMode: "PathOfKings.BountyHunter",
            prioritizeTargetRare: prioritize);

        Assert.True(prioritize);
        Assert.Equal("희귀함", BaseTier(
            catalog.Unit(recommendations[0].Route.GoalUnitId).Tier));
        Assert.Equal(goalId, recommendations[0].ProgressionGoalUnitId);
    }

    [Fact]
    public void GarpGoalRecommendsOnlyNearestMissingTargetRareAfterFirstRare()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var engine = new RecommendationEngine(catalog);
        const string goalId = "rawcode:C40h";
        var targetTree = RecipeTreeIds(catalog, goalId);
        var targetRares = engine.RecipeRareUnitIds(goalId).ToList();
        Assert.True(targetRares.Count > 1);

        var recommendations = engine.RecommendNearestCrafts(goalId,
            [new InventoryEntry { UnitId = targetRares[0], Count = 1 }],
            navigationMode: "PathOfKings.BountyHunter",
            prioritizeTargetRare: true);
        var inventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [targetRares[0]] = 1
        };
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        var expectedNearestRareId = targetRares
            .Where(id => !id.Equals(targetRares[0], StringComparison.OrdinalIgnoreCase))
            .Select(id => (Id: id, Progress: calculator.Calculate([id], inventory)))
            .OrderBy(item => item.Progress.MissingLeaves.Sum(leaf => leaf.MissingCount))
            .ThenByDescending(item => item.Progress.CompletionRatio)
            .First().Id;

        var first = recommendations[0];
        var firstUnit = first.CompositionUnits[0];
        Assert.True(BaseTier(firstUnit.Tier) == "희귀함",
            $"target rares={string.Join(',', targetRares)}; recommendations=" +
            string.Join(',', recommendations.Select(item =>
                $"{item.Route.GoalUnitId}:{BaseTier(item.CompositionUnits[0].Tier)}")));
        Assert.Contains(firstUnit.UnitId, targetTree);
        Assert.NotEqual(targetRares[0], firstUnit.UnitId);
        Assert.Equal(expectedNearestRareId, firstUnit.UnitId);
        var rareCards = recommendations
            .Where(item => BaseTier(catalog.Unit(item.Route.GoalUnitId).Tier) == "희귀함")
            .ToList();
        Assert.True(rareCards.Count == 1,
            "희귀함 카드는 목표 연계 최단 한 장이어야 함: " +
            string.Join(", ", rareCards.Select(item => item.Route.GoalUnitId)));
        Assert.Equal(goalId, first.ProgressionGoalUnitId);
        Assert.Equal("거프 불멸", first.ProgressionGoalName);
        Assert.Contains(first.RemainingCraftSteps, step => step.UnitId == firstUnit.UnitId);
        Assert.Contains(first.RemainingCraftSteps, step => step.UnitId == goalId);
        Assert.True(
            first.RemainingCraftSteps.FindIndex(step => step.UnitId == firstUnit.UnitId) <
            first.RemainingCraftSteps.FindIndex(step => step.UnitId == goalId));

        var recascaded = engine.Recascade(recommendations,
            [new InventoryEntry { UnitId = targetRares[0], Count = 1 }],
            first.Route.Id);
        var recascadedRareCards = recascaded
            .Where(item => BaseTier(catalog.Unit(item.Route.GoalUnitId).Tier) == "희귀함")
            .ToList();
        Assert.True(recascadedRareCards.Count == 1,
            "재계산 후에도 희귀함 카드는 한 장이어야 함: " +
            string.Join(", ", recascadedRareCards.Select(item => item.Route.GoalUnitId)));
        Assert.Equal(goalId, recascaded[0].ProgressionGoalUnitId);
        Assert.Contains(recascaded[0].RemainingCraftSteps, step => step.UnitId == goalId);
    }

    private static HashSet<string> RecipeTreeIds(DataCatalog catalog, string rootId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(rootId);
        return result;

        void Add(string unitId)
        {
            if (!result.Add(unitId)) return;
            foreach (var childId in catalog.Unit(unitId).Recipe.Keys)
                Add(childId);
        }
    }

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}
