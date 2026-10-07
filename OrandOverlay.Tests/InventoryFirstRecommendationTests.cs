using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class InventoryFirstRecommendationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadyUnrecordedSlowSupportIsNotDisplacedByPopularDistantSupport(bool hasHistory)
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var goal = catalog.Unit("rawcode:Q40h");
        var ready = catalog.Unit("rawcode:K50h");
        var popular = catalog.Unit("rawcode:W50h");
        var stats = ClearBuildStats.FromSamples(Enumerable.Range(0, 20).Select(index =>
            new ClearSample($"inventory-first-{index}", DateTimeOffset.UnixEpoch, "악몽", 2,
            [new ClearSampleUnit(goal.Rawcodes[0], 1, goal.Tier),
             new ClearSampleUnit(popular.Rawcodes[0], 1, popular.Tier)])));
        var inventory = ready.Recipe.Select(pair => new InventoryEntry
        {
            UnitId = pair.Key, Count = pair.Value
        }).Append(new InventoryEntry { UnitId = goal.Id, Count = 1 }).ToList();
        var engine = new RecommendationEngine(catalog, hasHistory ? stats : ClearBuildStats.Empty);
        var initialProgress = new RecipeCompletionCalculator(catalog.Unit).Calculate(
            [ready.Id], inventory.ToDictionary(entry => entry.UnitId, entry => entry.Count));

        var recommendations = engine.RecommendNearestCrafts(goal.Id, inventory, take: 12);
        var slow = recommendations.Where(item =>
            GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(item.Route.GoalUnitId)).Slow > 0)
            .ToList();

        Assert.Null(stats.Evidence(goal.Rawcodes, ready.Rawcodes));
        Assert.NotEmpty(slow);
        Assert.True(slow[0].Route.GoalUnitId == ready.Id,
            string.Join(" > ", slow.Select(item => $"{item.Route.Name}:{item.RecipeProgress.CompletionRatio}")));
        Assert.Equal(1, initialProgress.CompletionRatio);
        Assert.Equal(0.5, slow[0].RecipeProgress.CompletionRatio);
    }
}
