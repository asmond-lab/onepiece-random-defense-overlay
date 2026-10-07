using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletCraftBlockExplanationTests
{
    [Fact]
    public void LaterLegendPipelineConsumesAuxiliaryChopperWithoutReplacement()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        const string target = "rawcode:B30h";
        const string intermediate = "rawcode:220h";
        var inventory = catalog.Unit(target).Recipe.Where(p => catalog.Unit(p.Key).Tier != "자원")
            .ToDictionary(p => p.Key, p => p.Value);
        var copies = inventory[intermediate];
        inventory.Remove(intermediate);
        foreach (var ingredient in catalog.Unit(intermediate).Recipe.Where(p => catalog.Unit(p.Key).Tier != "자원"))
            inventory[ingredient.Key] = inventory.GetValueOrDefault(ingredient.Key) + ingredient.Value * copies;
        inventory["rawcode:530h"] = 1; // This is later progression, not the opening exemption.
        var plan = new BulletGuidePlan(BulletGuideStage.SecondLegend, target, false)
            { Round = 19, KnownLegendLowerBound = 1 };
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));

        CoachDecision Decide()
        {
            var entries = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray();
            var recommendations = new RecommendationEngine(catalog, combineHotkeys: hotkeys)
                .RecommendGuideCraft(target, entries, plan, 19, 8);
            var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(recommendations, entries);
            return new BeginnerCoachPlanner(catalog).Decide(new CoachFrame
            {
                Mode = PlayMode.Guide, GuideNumber = 1, GuidePlan = plan,
                MatchGeneration = 1, Revision = 1, Round = 19, CompletedStoryStage = 8,
                IsCurrent = true, Difficulty = "신", GoalId = BulletGuidePolicy.GoalId,
                Inventory = inventory.ToImmutableDictionary(), Recommendations = recommendations, CraftSteps = steps,
                Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100).Add("gold", 100000)
            });
        }

        Assert.True(BulletGuideCraftSafety.Allows(catalog, intermediate, inventory, 19, null));
        var resumed = Decide();
        Assert.Equal(CoachActionKind.Craft, resumed.Kind);
        Assert.Equal(intermediate, resumed.TargetUnitId);
    }
}
