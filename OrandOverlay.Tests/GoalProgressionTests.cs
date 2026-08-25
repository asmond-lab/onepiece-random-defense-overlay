using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GoalProgressionTests
{
    [Fact]
    public void UnknownRoundInventoryGrowthKeepsUnseenFirstRare()
    {
        var gate = new FirstRareRecommendationGate();
        var targetRares = new[] { "rawcode:L20h" };

        Assert.True(gate.ShouldPrioritize(
            "rawcode:A90H", InventoryCount(20), targetRares, currentRound: 0));
        Assert.True(gate.ShouldPrioritize(
            "rawcode:A90H", InventoryCount(21), targetRares, currentRound: 0));
    }

    [Fact]
    public void ActiveMatchStartingAboveFallbackThresholdPrioritizesFirstRare()
    {
        var gate = new FirstRareRecommendationGate();

        Assert.True(gate.ShouldPrioritize(
            "rawcode:A90H", InventoryCount(21), ["rawcode:L20h"],
            currentRound: 0, matchActive: true));
    }

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
        Assert.DoesNotContain(first.RemainingCraftSteps, step => step.UnitId == goalId);
        var focusedRareTree = RecipeTreeIds(catalog, firstUnit.UnitId);
        Assert.All(first.RemainingCraftSteps,
            step => Assert.Contains(step.UnitId, focusedRareTree));

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
        Assert.DoesNotContain(recascaded[0].RemainingCraftSteps,
            step => step.UnitId == goalId);
    }

    [Fact]
    public void ReadyBrookRareKeepsOnlyBrookCraftFlowForJinbeGoal()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        const string brookRareId = "rawcode:N10h";
        var missing = calculator.Calculate([brookRareId],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        var inventory = missing.Leaves
            .Select(leaf => new InventoryEntry
            {
                UnitId = leaf.UnitId,
                Count = checked((int)leaf.RequiredCount)
            })
            .ToList();
        var engine = new RecommendationEngine(catalog);

        var first = engine.RecommendNearestCrafts("rawcode:A90H", inventory,
            navigationMode: "PathOfKings.BountyHunter",
            prioritizeTargetRare: true)[0];

        Assert.Equal(brookRareId, first.Route.GoalUnitId);
        Assert.Equal(1, first.RecipeProgress.CompletionRatio);
        Assert.Contains(first.RemainingCraftSteps,
            step => step.UnitId == brookRareId);
        var brookTree = RecipeTreeIds(catalog, brookRareId);
        Assert.All(first.RemainingCraftSteps,
            step => Assert.Contains(step.UnitId, brookTree));
        Assert.DoesNotContain(first.RemainingCraftSteps,
            step => step.UnitId == "rawcode:A90H");
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

    private static IReadOnlyList<InventoryEntry> InventoryCount(int count) =>
        [new InventoryEntry { UnitId = "rawcode:100h", Count = count }];
}
