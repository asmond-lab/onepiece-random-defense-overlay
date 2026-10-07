using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachRecipeGuidanceTests
{
    [Fact]
    public void CompleteLeafMaterialsWithoutExecutableStepWaitsRatherThanGatheringMore()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["goal"] = new UnitDefinition { Id = "goal", Name = "goal", Recipe = new() { ["material"] = 1 } };
        units["material"] = new UnitDefinition { Id = "material", Name = "material" };
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with
        {
            Mode = PlayMode.Normal, Round = 9, CompletedStoryStage = 7, GoalId = null,
            CraftSteps = [],
            Recommendations = [new Recommendation
            {
                Route = new RouteDefinition { Id = "craft:goal", GoalUnitId = "goal", Name = "goal" },
                RecipeProgress = new RecipeCompletionCalculator(catalog.Unit).Calculate(["goal"],
                    ImmutableDictionary<string, int>.Empty.Add("material", 1))
            }]
        };
        Assert.Equal(1, frame.Recommendations[0].RecipeProgress.CompletionRatio);
        Assert.Equal(CoachActionKind.Waiting, new BeginnerCoachPlanner(catalog).Decide(frame).Kind);
    }

    [Fact]
    public void PreviewPreservesAllFourFujitoraStagesAndCatalogResourcesWithoutAuthorizingCraft()
    {
        var (catalog, frame) = FujitoraFrame();
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Waiting, decision.Kind);
        Assert.Null(decision.CraftRecipe);
        Assert.Equal(4, decision.RecipePreview.Count);
        Assert.Equal(frame.Recommendations[0].RemainingCraftSteps.Select(step => step.UnitId),
            decision.RecipePreview.Select(step => step.UnitId));
        Assert.Contains(decision.RecipePreview, step => step.UnitId == "rawcode:130h");
        foreach (var step in decision.RecipePreview)
        {
            Assert.Null(step.CombineKey);
            Assert.Empty(step.CombineCommands);
            Assert.Equal(catalog.Unit(step.UnitId).Recipe.OrderBy(pair => pair.Key),
                step.Ingredients.ToDictionary(item => item.UnitId, item => item.RequiredCount / step.MissingCount)
                    .OrderBy(pair => pair.Key));
        }
    }

    [Theory]
    [InlineData(StorySequenceAction.WaitForStoryReward, CoachActionKind.Story, null)]
    [InlineData(StorySequenceAction.SpendStoryWisps, CoachActionKind.Reward, "e016")]
    [InlineData(StorySequenceAction.SpendRareWisps, CoachActionKind.Reward, "e019")]
    public void RewardHoldKeepsPreviewButNeverAnExecutableRecipe(StorySequenceAction action, CoachActionKind kind, string? wisp)
    {
        var (catalog, frame) = FujitoraFrame();
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame with
        {
            Story = new StoryRewardSequenceDecision(RecommendationSequenceStage.StoryReward,
                action, "", "", "", "", "", null, null, 0, false),
            RewardWisps = wisp is null ? ImmutableDictionary<string, int>.Empty :
                ImmutableDictionary<string, int>.Empty.Add(wisp, 1)
        });
        Assert.Equal(kind, decision.Kind);
        Assert.True(decision.CraftDeferredForReward);
        Assert.Null(decision.CraftRecipe);
        Assert.Equal(4, decision.RecipePreview.Count);
    }

    [Fact]
    public void StaleAndPausedFramesNeverReuseRecipePreview()
    {
        var (catalog, frame) = FujitoraFrame();
        var session = new BeginnerCoachSession(catalog);
        Assert.NotEmpty(session.Update(frame).RecipePreview);
        Assert.Empty(session.Update(frame with { Revision = 2, IsCurrent = false }).RecipePreview);
        Assert.Empty(session.Update(frame with { Revision = 3, Paused = true }).RecipePreview);
    }

    [Theory]
    [InlineData(0, CoachActionKind.Economy)]
    [InlineData(3, CoachActionKind.Craft)]
    public void SafeCatalogStepShowsActualRecipeAndStillChecksResources(int lumber, CoachActionKind kind)
    {
        var (catalog, _) = FujitoraFrame();
        var goal = catalog.Unit("rawcode:130h");
        var inventory = goal.Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원")
            .Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var recommendation = new RecommendationEngine(catalog, combineHotkeys: hotkeys)
            .RecommendNearestCrafts(goal.Id, inventory, difficulty: "신", round: 13, completedStoryStage: 7)
            .Single(item => item.Route.GoalUnitId == goal.Id);
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan([recommendation], inventory);
        var action = Assert.Single(steps);
        Assert.Equal(hotkeys.FindByResult(goal.Rawcodes)!.Key, action.Key);
        var decision = new BeginnerCoachPlanner(catalog).Decide(BeginnerCoachPlannerTests.ReadyFrame() with
        {
            Round = 13, CompletedStoryStage = 7, Difficulty = "신", GoalId = null,
            Inventory = inventory.ToImmutableDictionary(item => item.UnitId, item => item.Count),
            CraftSteps = steps, Recommendations = [recommendation],
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", lumber)
        });
        Assert.Equal(kind, decision.Kind);
        Assert.Equal(action.TargetUnitId, decision.CraftRecipe!.UnitId);
        Assert.Equal(goal.Recipe.OrderBy(pair => pair.Key), decision.CraftRecipe.Ingredients
            .ToDictionary(item => item.UnitId, item => item.RequiredCount).OrderBy(pair => pair.Key));
        Assert.Contains(action.Key, decision.Controls);
    }

    private static (DataCatalog, CoachFrame) FujitoraFrame()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:130h");
        var inventory = goal.Recipe.Keys.Select(catalog.Unit).Where(unit => unit.Tier != "자원")
            .SelectMany(unit => unit.Recipe).GroupBy(pair => pair.Key)
            .Select(group => new InventoryEntry { UnitId = group.Key, Count = group.Sum(pair => pair.Value) }).ToArray();
        var recommendation = new RecommendationEngine(catalog)
            .RecommendNearestCrafts(goal.Id, inventory, difficulty: "신", round: 9, completedStoryStage: 7)
            .Single(item => item.Route.GoalUnitId == goal.Id);
        return (catalog, BeginnerCoachPlannerTests.ReadyFrame() with
        {
            Mode = PlayMode.Normal, Round = 9, CompletedStoryStage = 7, Difficulty = "신", GoalId = null,
            Inventory = inventory.ToImmutableDictionary(item => item.UnitId, item => item.Count),
            CraftSteps = [], Recommendations = [recommendation]
        });
    }
}
