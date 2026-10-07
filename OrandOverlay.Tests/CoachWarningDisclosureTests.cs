using System.Collections.Immutable;
using Xunit;
using Xunit.Abstractions;
namespace OrandOverlay.Tests;
public sealed class CoachWarningDisclosureTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProductionRecommendationWarningSurvivesCompactCraftAndGather(bool craft)
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        foreach (var target in new[] { craft ? "bartolomeo_legend" : "rawcode:F40h" })
        {
            var unit = catalog.Unit(target);
            var inventory = unit.Recipe.Where(p => catalog.Unit(p.Key).Tier != "자원")
                .Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToList();
            var recommendations = new RecommendationEngine(catalog, combineHotkeys: hotkeys)
                .RecommendNearestCrafts("rawcode:F40h", inventory, difficulty: "신", round: 13, completedStoryStage: 7);
            var frame = BeginnerCoachPlannerTests.ReadyFrame() with
            {
                Mode = PlayMode.Normal, Round = 13, CompletedStoryStage = 7, Difficulty = "신", GoalId = null,
                Inventory = inventory.ToImmutableDictionary(p => p.UnitId, p => p.Count),
                Recommendations = recommendations, CraftSteps = new AutoCombinePlanner(catalog, hotkeys).Plan(recommendations, inventory),
                Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100)
            };
            var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
            output.WriteLine($"{target} {decision.Kind} {decision.Reason}");
            if (decision.Kind != (craft ? CoachActionKind.Craft : CoachActionKind.Gather) || recommendations[0].Warnings.Count == 0) continue;
            Assert.False(decision.IsUrgent); Assert.False(decision.CraftDeferredForReward); Assert.Empty(decision.Constraint);
            Assert.Equal(recommendations[0].Warnings[0], decision.Reason);
            Assert.True(CoachPresentation.Create(decision, frame).ShowEssentialReason,
                $"Actual {decision.Kind} warning hidden: {decision.Reason}");
            return;
        }
        Assert.Fail("No matching production warning fixture found.");
    }
}
