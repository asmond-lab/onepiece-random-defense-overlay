using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RareRerollAdvisorTests
{
    [Fact]
    public void SanjiRound44CleansUnusedRyumaAndOarsRares()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var stats = ClearBuildStats.Load(
            [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")]);
        var inventory = new[]
        {
            new InventoryEntry { UnitId = "rawcode:H90H" },
            new InventoryEntry { UnitId = "rawcode:520h", Count = 2 },
            new InventoryEntry { UnitId = "rawcode:020h" }
        };
        var recommendations = new RecommendationEngine(catalog, stats)
            .RecommendNearestCrafts(
                "rawcode:H90H", inventory, take: 12,
                navigationMode: "PathOfKings.BountyHunter");
        var advisor = new RareRerollAdvisor(catalog);
        var goal = catalog.Unit("rawcode:H90H");

        var earlyAdvice = advisor.Evaluate(
            inventory, recommendations, goal, stats);
        Assert.DoesNotContain(earlyAdvice,
            item => item.UnitId is "rawcode:520h" or "rawcode:020h");

        var lateAdvice = advisor.Evaluate(
            inventory, recommendations, goal, stats, round: 44);

        Assert.Contains(lateAdvice,
            item => item.UnitId == "rawcode:520h" && item.RerollCount == 2);
        Assert.Contains(lateAdvice,
            item => item.UnitId == "rawcode:020h" && item.RerollCount == 1);
    }
}
