using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletReservationFixTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SharedLeafHasStableOwnerAndAProgressingPipeline(int copies)
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "M20h", "N10h", "J10h", "A10h", "X00h", "M10h", "410h", "510h" }
            .ToImmutableDictionary(x => "rawcode:" + x, x => x == "X00h" ? copies : 1);
        string[] roots = ["rawcode:MC0h", "rawcode:O30h"];
        var first = BulletGuideReservations.Available(catalog, inventory, roots, roots[0]);
        var second = BulletGuideReservations.Available(catalog, inventory, roots.Reverse(), roots[1]);
        Assert.Equal(1, first.GetValueOrDefault("rawcode:X00h"));
        Assert.Equal(copies - 1, second.GetValueOrDefault("rawcode:X00h"));
        // Auxiliary A10h slow does not demand a replacement before the main recipe.
        Assert.True(BulletGuideCraftSafety.Allows(catalog, "rawcode:P10h", inventory, 11, null));
        var plan = new BulletGuidePolicy(catalog).Plan(11, 5, inventory, "악몽");
        Assert.Equal("rawcode:MC0h", plan.TargetUnitId);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var entries = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 11, CompletedStoryStage = 5
        });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        Assert.True(steps.Any(s => s.TargetUnitId == "rawcode:P10h"),
            System.Text.Json.JsonSerializer.Serialize(new { candidates.Recommendations, safe = BulletGuideCraftSafety.Allows(catalog, "rawcode:P10h", inventory, 11, null) }));
        var decision = new BeginnerCoachPlanner(catalog).Decide(new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, Round = 11, CompletedStoryStage = 5,
            IsCurrent = true, Difficulty = "악몽", Inventory = inventory, GuidePlan = plan,
            MatchGeneration = 1, Revision = 1, CraftSteps = steps, Recommendations = candidates.Recommendations,
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100)
        });
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal("rawcode:P10h", decision.TargetUnitId);
    }

    [Theory]
    [InlineData("O20h", CoachActionKind.Waiting)]
    [InlineData("B30h", CoachActionKind.Craft)]
    public void WhitebeardRootRejectsStaleAceButAllowsItsOwnCraft(string action, CoachActionKind expected)
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "220h", "Y10h", "120h", "E20h", "E20h", "J20h" }
            .GroupBy(x => "rawcode:" + x).ToImmutableDictionary(g => g.Key, g => g.Count());
        var plan = new BulletGuidePolicy(catalog).Plan(11, 5, inventory, "악몽");
        Assert.Equal("rawcode:B30h", plan.TargetUnitId);
        Assert.Contains("rawcode:B30h", plan.ProtectedUnitIds);
        var decision = new BeginnerCoachPlanner(catalog).Decide(new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, Round = 11, CompletedStoryStage = 5,
            IsCurrent = true, Difficulty = "악몽", Inventory = inventory, GuidePlan = plan,
            MatchGeneration = 1, Revision = 1,
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100),
            CraftSteps = [new("rawcode:" + action, catalog.Unit("rawcode:" + action).Name, "rawcode:220h", "마르코", "220h", "X", [])]
        });
        Assert.Equal(expected, decision.Kind);
    }
}
