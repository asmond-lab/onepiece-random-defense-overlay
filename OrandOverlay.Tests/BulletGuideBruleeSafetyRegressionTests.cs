using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideBruleeSafetyRegressionTests
{
    private readonly DataCatalog _catalog = new();
    public BulletGuideBruleeSafetyRegressionTests() => _catalog.Load(loadCarryPolicy: false);

    private static Dictionary<string, int> Inventory() =>
        new[] { "HA0h", "MC0h", "C20h", "O10h", "X50h", "010h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void ActualGuidePipelineAndCoachRespectObservedBruleeWood(int wood, bool craft)
    {
        var counts = Inventory();
        var inventory = counts.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var plan = new BulletGuidePolicy(_catalog).Plan(16, 9, counts, "악몽");
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(_catalog, combineHotkeys: hotkeys),
            Goal = _catalog.Unit(BulletGuidePolicy.GoalId), Inventory = inventory,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 16, CompletedStoryStage = 9
        });
        var steps = new AutoCombinePlanner(_catalog, hotkeys).Plan(candidates.Recommendations, inventory);
        Assert.Contains(steps, step => step.TargetUnitId == "rawcode:S80h");
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
        {
            Mode = PlayMode.Guide, GuideNumber = 1, GoalId = BulletGuidePolicy.GoalId,
            Round = 16, CompletedStoryStage = 9, ConfirmedNavigation = null,
            Inventory = counts.ToImmutableDictionary(), GuidePlan = plan, CraftSteps = steps,
            Recommendations = candidates.Recommendations,
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", wood)
        };
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);
        Assert.Equal(craft, decision.Kind == CoachActionKind.Craft);
        Assert.Equal("rawcode:S80h", decision.TargetUnitId);
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(16, true)]
    public void RecipeExceptionDoesNotApplyAfterBullet(int round, bool bullet)
    {
        var inventory = Inventory();
        if (bullet) inventory[BulletGuidePolicy.GoalId] = 1;
        Assert.False(BulletGuideCraftSafety.Allows(_catalog, "rawcode:S80h", inventory, round, null));
    }

    [Fact]
    public void SourcePreparationPreservesAurasButMayInvestItsLastRecipeRareStun()
    {
        var inventory = Inventory();
        var plan = new BulletGuidePolicy(_catalog).Plan(16, 9, inventory, "악몽");
        Assert.Equal(BulletGuideStage.BruleePreparation, plan.Stage);
        Assert.Equal("rawcode:S80h", plan.TargetUnitId);
        var allocation = new RecipeCompletionCalculator(_catalog.Unit).CalculateAllocation([plan.TargetUnitId!], inventory);
        var after = allocation.RemainingInventory.ToDictionary(pair => pair.Key, pair => checked((int)pair.Value));
        after[plan.TargetUnitId!] = 1;
        var support = new BulletGuideSupportPolicy(_catalog);
        var beforeSupport = support.Evaluate(inventory, null, GoroseiMode.None);
        var afterSupport = support.Evaluate(after, null, GoroseiMode.None);
        Assert.Equal(27, beforeSupport.ArmorPotential);
        Assert.Equal(10, beforeSupport.SlowPotential);
        Assert.Equal(beforeSupport.ArmorPotential, afterSupport.ArmorPotential);
        Assert.Equal(beforeSupport.SlowPotential, afterSupport.SlowPotential);
        Assert.False(beforeSupport.StunPairReady);
        Assert.False(afterSupport.StunPairReady);
        Assert.Equal(0, after.GetValueOrDefault("rawcode:C20h"));
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, plan.TargetUnitId!, inventory, 16, null));
    }
}
