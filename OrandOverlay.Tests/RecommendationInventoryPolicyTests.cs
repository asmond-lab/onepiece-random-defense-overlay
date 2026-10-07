using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationInventoryPolicyTests
{
    [Fact]
    public void GreenBloodUsedOnUnitAddsPointThreeStunToRecommendationStats()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var inventory = RecommendationInventoryPolicy.Build(
            [], preserveCompleted: false, new CompletedTopUnitTracker(catalog),
            includeGreenBloodBuff: true);

        var stats = new InventoryStatsCalculator(catalog).Calculate(inventory);

        Assert.Contains(inventory, entry =>
            entry.UnitId == "greenblood_buff" && entry.Count == 1);
        Assert.Equal(0.3, stats.Stun, precision: 3);
    }

    [Fact]
    public void CompletedZoroLegendRemainsOwnedDuringBoardInteraction()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var tracker = new CompletedTopUnitTracker(catalog);
        tracker.ObserveGoalCraft("rawcode:F90H",
            [new InventoryEntry { UnitId = "rawcode:S20h", Count = 1 }]);
        var engine = new RecommendationEngine(catalog);

        var staleZoroLegend = Assert.Single(
            engine.StoryClusterChildren("rawcode:F90H", []),
            child => child.Route.GoalUnitId == "rawcode:S20h");
        Assert.Contains(staleZoroLegend.RemainingCraftSteps,
            step => step.UnitId == "rawcode:010h");
        Assert.Contains(staleZoroLegend.RecipeProgress.MissingLeaves,
            leaf => leaf.UnitId == "rawcode:H00h");

        var inventory = RecommendationInventoryPolicy.Build(
            [], preserveCompleted: true, tracker, includeGreenBloodBuff: false);

        Assert.Contains(inventory, entry =>
            entry.UnitId == "rawcode:S20h" && entry.Count == 1);
        Assert.DoesNotContain(
            engine.StoryClusterChildren("rawcode:F90H", inventory),
            child => child.Route.GoalUnitId == "rawcode:S20h");
    }
}
