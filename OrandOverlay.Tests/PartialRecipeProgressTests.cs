using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class PartialRecipeProgressTests
{
    private readonly DataCatalog catalog = BulletGuideUncommonSaleTests.Catalog();
    private readonly CombineHotkeyCatalog hotkeys = CombineHotkeyCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MissingDuplicateNeedsOnlyOneCanonicalRecipe(int owned)
    {
        var inventory = catalog.Unit("rawcode:V00h").Recipe.ToDictionary();
        inventory["rawcode:V00h"] = owned;
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(
            [Intermediate("rawcode:V00h", owned + 2, owned)], Entries(inventory));
        Assert.Equal("rawcode:V00h", Assert.Single(steps).TargetUnitId);
        var after = Assert.IsType<Dictionary<string, int>>(BulletGuideCraftSafety.ProjectAfterCraft(catalog, "rawcode:V00h", inventory));
        Assert.Equal(owned + 1, after["rawcode:V00h"]);
        Assert.All(catalog.Unit("rawcode:V00h").Recipe.Keys, id => Assert.Equal(0, after[id]));
        Assert.Empty(new AutoCombinePlanner(catalog, hotkeys).Plan(
            [Intermediate("rawcode:V00h", owned + 2, owned + 1)], Entries(after)));
    }

    [Fact]
    public void OneCraftLeavesExactlyOneRecipeWorthForAnotherTarget()
    {
        var inventory = new Dictionary<string, int>
        {
            ["rawcode:F00h"] = 2, ["rawcode:200h"] = 2, ["rawcode:600h"] = 3
        };
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(
            [Intermediate("rawcode:V00h", 2, 0), Intermediate("rawcode:A00h", 1, 0)], Entries(inventory));
        Assert.Equal(new[] { "rawcode:V00h", "rawcode:A00h" }, steps.Select(step => step.TargetUnitId));
        var after = Assert.IsType<Dictionary<string, int>>(BulletGuideCraftSafety.ProjectAfterCraft(catalog, "rawcode:V00h", inventory));
        Assert.Equal(1, after["rawcode:F00h"]);
        Assert.Equal(1, after["rawcode:200h"]);
        Assert.Equal(2, after["rawcode:600h"]);
        Assert.Equal(1, after["rawcode:V00h"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOrCompletedRootIsNotCraftedAgain(bool completed)
    {
        var inventory = catalog.Unit("rawcode:F00h").Recipe.ToDictionary();
        if (!completed) inventory["rawcode:F00h"] = 1;
        // Stale missing step must not override actual root ownership or completion.
        var step = Intermediate("rawcode:F00h", 1, 0).RemainingCraftSteps.Single();
        var recommendation = new Recommendation
        {
            Route = new() { Id = "root", Name = "root", GoalUnitId = step.UnitId },
            RemainingCraftSteps = [step]
        };
        Assert.Empty(new AutoCombinePlanner(catalog, hotkeys).Plan([recommendation], Entries(inventory),
            completedUnitIds: completed ? [step.UnitId] : []));
    }

    [Fact]
    public void Sequence365RecomputesThroughObservedWhitebeardAndNextStage()
    {
        // Exact sequence-365 inventory/resources; no machine-local replay dependency.
        var inventory = new Dictionary<string, int>
        {
            ["luffy_common"] = 3, ["rawcode:320h"] = 1, ["rawcode:N00h"] = 2,
            ["rawcode:J10h"] = 1, ["rawcode:U10h"] = 1, ["rawcode:R00h"] = 1,
            ["rawcode:500h"] = 3, ["rawcode:210h"] = 1, ["rawcode:200h"] = 3,
            ["rawcode:C00h"] = 1, ["rawcode:F00h"] = 1, ["rawcode:D20h"] = 1,
            ["rawcode:800h"] = 1, ["rawcode:410h"] = 1, ["rawcode:700h"] = 3,
            ["rawcode:600h"] = 2, ["rawcode:X00h"] = 1, ["rawcode:110h"] = 1,
            ["rawcode:L50h"] = 2, ["rawcode:900h"] = 1, ["rawcode:C10h"] = 1,
            ["rawcode:D10h"] = 1, ["rawcode:100h"] = 1, ["rawcode:B10h"] = 1,
            ["rawcode:W00h"] = 1, ["rawcode:400h"] = 5, ["rawcode:G20h"] = 2,
            ["rawcode:D00h"] = 1, ["rawcode:910h"] = 1, ["rawcode:T00h"] = 1,
            ["rawcode:E00h"] = 2
        }.ToImmutableDictionary();
        var signals = new Dictionary<string, long?>
        {
            ["boss-hp"] = null, ["upgrade-level"] = null, ["gold"] = 46327,
            ["boss-limit-seconds"] = null, ["upgrade-cost"] = null, ["lumber"] = 16,
            ["reroll-cost"] = null, ["lives"] = null, ["line-count"] = null, ["trait-points"] = 3
        }.ToImmutableDictionary();
        var history = new Dictionary<string, int>();
        var chain = new List<string>();
        for (var iteration = 0; iteration < 16; iteration++)
        {
            var guide = new BulletGuidePolicy(catalog).Plan(17, 8, inventory, "신",
                observedLegendCounts: history);
            if (inventory.GetValueOrDefault("rawcode:B30h") == 1)
            {
                Assert.Equal(BulletGuideStage.SecondLegend, guide.Stage);
                Assert.Equal(1, guide.KnownLegendLowerBound);
                break;
            }
            Assert.Equal(BulletGuideStage.FirstLegend, guide.Stage);
            Assert.Equal("rawcode:B30h", guide.TargetUnitId);
            var entries = Entries(inventory);
            var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
            {
                Mode = PlayMode.Guide, GuidePlan = guide,
                Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
                Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
                InitialSurface = RecommendationSurface.TopAndNavigation,
                NavigationMode = BulletGuidePolicy.NavigationId, Gorosei = GoroseiMode.None,
                BuildVariant = BuildVariants.AutoId, Difficulty = "신", Round = 17, CompletedStoryStage = 8
            });
            Assert.Equal(1, Assert.Single(candidates.Recommendations).RecipeProgress.CompletionRatio);
            var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries,
                protectedUnitIds: guide.ProtectedUnitIds);
            var decision = new BeginnerCoachPlanner(catalog).Decide(new CoachFrame
            {
                Mode = PlayMode.Guide, GuideNumber = 1, Round = 17, CompletedStoryStage = 8,
                IsCurrent = true, Difficulty = "신", Inventory = inventory, GuidePlan = guide,
                MatchGeneration = 196, Revision = 5615 + iteration, Signals = signals,
                CraftSteps = steps, Recommendations = candidates.Recommendations
            });
            Assert.True(decision.Kind == CoachActionKind.Craft,
                string.Join(" -> ", chain) + " | " + decision.Id + " | " + decision.Reason);
            var action = steps.First();
            Assert.Equal(action.TargetUnitId, decision.TargetUnitId);
            chain.Add(action.TargetUnitId);
            var after = Assert.IsType<Dictionary<string, int>>(BulletGuideCraftSafety.ProjectAfterCraft(
                catalog, action.TargetUnitId, inventory));
            foreach (var cost in catalog.Unit(action.TargetUnitId).Recipe.Where(p => catalog.Unit(p.Key).Tier == "자원"))
            {
                var key = cost.Key.Replace("rawcode:", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
                Assert.True(signals[key] >= cost.Value);
                signals = signals.SetItem(key, signals[key] - cost.Value);
            }
            Assert.All(after.Values, count => Assert.True(count >= 0));
            inventory = after.ToImmutableDictionary();
            if (TopGradePolicy.BaseTier(catalog.Unit(action.TargetUnitId).Tier) is "전설" or "히든" or "해적선")
                history[action.TargetUnitId] = inventory[action.TargetUnitId];
        }
        Assert.Equal(1, inventory.GetValueOrDefault("rawcode:B30h"));
        Assert.Equal(13L, signals["lumber"]);
        Assert.Equal(46327L, signals["gold"]);
        var next = new BulletGuidePolicy(catalog).Plan(17, 8, inventory, "신", observedLegendCounts: history);
        Assert.Equal(BulletGuideStage.SecondLegend, next.Stage);
        Assert.Equal(1, next.KnownLegendLowerBound);
        Assert.Equal(new[] { "rawcode:F00h", "rawcode:V00h", "rawcode:V00h", "rawcode:220h",
            "rawcode:E20h", "rawcode:J20h", "rawcode:B30h" }, chain);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("historical")]
    [InlineData("unrelated")]
    [InlineData("later")]
    public void FirstLegendRetentionExceptionRejectsStaleOrUnrelatedContext(string context)
    {
        const string target = "rawcode:B30h";
        var inventory = catalog.Unit(target).Recipe
            .Where(pair => catalog.Unit(pair.Key).Tier != "자원").ToDictionary();
        var plan = new BulletGuidePlan(BulletGuideStage.FirstLegend, "rawcode:B30h", false);
        Assert.True(BulletGuideCraftSafety.Allows(catalog, target, inventory, 17, null, plan: plan));
        switch (context)
        {
            case "current": inventory["rawcode:530h"] = 1; break;
            case "historical": plan = plan with { KnownLegendLowerBound = 1 }; break;
            case "unrelated": plan = plan with { TargetUnitId = "rawcode:HA0h" }; break;
            case "later": plan = plan with { Stage = BulletGuideStage.SecondLegend }; break;
        }
        Assert.False(BulletGuideCraftSafety.Allows(catalog, target, inventory, 17, null, plan: plan));
        var recommendations = new RecommendationEngine(catalog, combineHotkeys: hotkeys)
            .RecommendGuideCraft("rawcode:B30h", Entries(inventory), plan, 17, 8);
        Assert.DoesNotContain(new AutoCombinePlanner(catalog, hotkeys).Plan(recommendations, Entries(inventory)),
            step => step.TargetUnitId == target);
        var decision = new BeginnerCoachPlanner(catalog).Decide(new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, GuidePlan = plan, Round = 17, CompletedStoryStage = 8,
            IsCurrent = true, Difficulty = "신", MatchGeneration = 1, Revision = 1,
            Inventory = inventory.ToImmutableDictionary(),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 16),
            CraftSteps = [new(target, catalog.Unit(target).Name, "rawcode:E20h", "Whitebeard", "E20h", "Z", [])]
        });
        Assert.NotEqual(CoachActionKind.Craft, decision.Kind);
    }

    private Recommendation Intermediate(string id, int required, int owned)
    {
        var unit = catalog.Unit(id);
        return new()
        {
            Route = new() { Id = "whitebeard", Name = "whitebeard", GoalUnitId = "rawcode:B30h" },
            RemainingCraftSteps = [new()
            {
                UnitId = id, Name = unit.Name, Tier = unit.Tier, RequiredCount = required, OwnedCount = owned,
                Ingredients = unit.Recipe.Select((pair, index) => new RecipeCraftIngredient
                {
                    UnitId = pair.Key, Name = catalog.Unit(pair.Key).Name,
                    RequiredCount = pair.Value * (required - owned), SelectionOrder = index
                }).ToList()
            }]
        };
    }

    private static InventoryEntry[] Entries(IReadOnlyDictionary<string, int> inventory) =>
        inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
}
