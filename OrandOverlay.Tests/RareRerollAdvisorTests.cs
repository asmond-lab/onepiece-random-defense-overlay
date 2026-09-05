using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RareRerollAdvisorTests
{
    [Theory]
    [InlineData(true, 0.5)]
    [InlineData(false, 1)]
    public void LateCleanupPreservesChosenGoalOrReadySupportMaterials(bool isGoal, double completion)
    {
        var catalog = new DataCatalog();
        catalog.Load();
        const string rareId = "rawcode:520h";
        var root = catalog.AllUnits.First(unit => unit.Recipe.ContainsKey(rareId));
        var count = root.Recipe[rareId];
        var recommendation = new Recommendation
        {
            Route = new RouteDefinition { Id = "ready", GoalUnitId = root.Id, Name = root.Name },
            RecipeProgress = new RecipeProgress
            {
                RequiredLeafCount = 2, OwnedLeafCount = (long)(completion * 2)
            }
        };
        var advice = new RareRerollAdvisor(catalog).Evaluate(
            [new InventoryEntry { UnitId = rareId, Count = count }], [recommendation],
            isGoal ? root : catalog.Unit("rawcode:H90H"), round: 44);

        Assert.DoesNotContain(advice, item => item.UnitId == rareId);
    }

    [Fact]
    public void EveryRareUsesConsistentLateCleanupClassification()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var rares = catalog.AllUnits
            .Where(unit => unit.Tier.Split('[', 2)[0].Trim() == "희귀함")
            .Where(unit => unit.Rawcodes.Count > 0)
            .DistinctBy(unit => unit.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(rares.Count >= 40);
        var dummyPlan = new[]
        {
            new Recommendation
            {
                Route = new RouteDefinition
                {
                    Id = "audit",
                    GoalUnitId = "item_greenblood",
                    Name = "감사"
                }
            }
        };
        var advisor = new RareRerollAdvisor(catalog);

        foreach (var rare in rares)
        {
            var advice = advisor.Evaluate(
                [new InventoryEntry { UnitId = rare.Id }],
                dummyPlan, round: RareRerollAdvisor.LateGameCleanupRound);
            if (RareRerollAdvisor.HasUtilityAbility(rare))
                Assert.DoesNotContain(advice, item => item.UnitId == rare.Id);
            else
                Assert.Contains(advice, item => item.UnitId == rare.Id);
        }
    }

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
