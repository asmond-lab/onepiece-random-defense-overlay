using Xunit;

namespace OrandOverlay.Tests;

public sealed class CurrentCraftSafetyTests
{
    [Fact]
    public void TacticalWarningsDoNotBecomeMissingSpecialMaterials()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:F40h");
        var inventory = goal.Recipe.Select(pair =>
            new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToList();
        var recommendation = new RecommendationEngine(catalog)
            .RecommendNearestCrafts(goal.Id, inventory, difficulty: "신", round: 40,
                completedStoryStage: 13)
            .Single(item => item.Route.GoalUnitId == goal.Id);

        Assert.True(recommendation.CurrentCraft!.LosesRequiredCombat);
        Assert.NotEmpty(recommendation.Warnings);
        Assert.Empty(recommendation.MissingSpecials);
    }

    [Fact]
    public void GabanConversionNamesReplacementSupportsAndResumesAfterTheyArrive()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:F40h");
        var inventory = goal.Recipe.Select(pair =>
            new InventoryEntry { UnitId = pair.Key, Count = pair.Value })
            .Append(new InventoryEntry { UnitId = "rawcode:540h" }).ToList();
        var engine = new RecommendationEngine(catalog);
        var first = engine.RecommendNearestCrafts(goal.Id, inventory,
            difficulty: "신", round: 40, completedStoryStage: 13);
        Assert.True(first.Single(item => item.Route.GoalUnitId == goal.Id)
            .CurrentCraft!.LosesRequiredCombat);
        Assert.NotEqual(goal.Id, first[0].Route.GoalUnitId);

        // Deterministic arrivals of the named replacement units, not a combat simulation.
        for (var step = 0; step < 8; step++)
        {
            var next = engine.RecommendNearestCrafts(goal.Id, inventory,
                difficulty: "신", round: 40, completedStoryStage: 13)[0];
            if (next.Route.GoalUnitId == goal.Id)
            {
                Assert.False(next.CurrentCraft!.LosesRequiredCombat);
                return;
            }
            Assert.DoesNotContain(inventory, item => item.UnitId == next.Route.GoalUnitId);
            inventory.Add(new InventoryEntry { UnitId = next.Route.GoalUnitId });
        }
        Assert.Fail("The named replacements did not unblock Gaban.");
    }

    [Fact]
    public void GabanPipelineGeneratesReadySurvivalActionBeforeFinalBuildCandidates()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:F40h");
        var support = catalog.Unit("rawcode:540h");
        var inventory = support.Recipe.Select(pair =>
            new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToList();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Engine = new RecommendationEngine(catalog),
            Goal = goal,
            Inventory = inventory,
            InitialSurface = RecommendationSurface.TopAndNavigation,
            NavigationMode = "PathOfKings.BountyHunter",
            Gorosei = GoroseiMode.None,
            BuildVariant = BuildVariants.AutoId,
            Difficulty = "신",
            Round = 30,
            CompletedStoryStage = 13,
            CandidateCount = 2
        });

        Assert.Equal(support.Id, candidates.Recommendations[0].Route.GoalUnitId);
        Assert.Equal(1, candidates.Recommendations[0].RecipeProgress.CompletionRatio);
    }

    [Fact]
    public void UnfinishedGabanDoesNotDisplaceReadyBossSupportAtRound30()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:F40h");
        var support = catalog.AllUnits
            .Where(unit => !TopGradePolicy.IsTopGrade(unit.Tier) && unit.Recipe.Count > 0)
            .Where(unit => GoalStrategyCalculator.IsCompatibleSupportDamageType(goal, unit))
            .Where(unit => GoalStrategyCalculator.StrategyMetricsFor(unit).BossControl > 0)
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .First();
        var inventory = support.Recipe.Select(pair =>
            new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToList();
        var engine = new RecommendationEngine(catalog);
        var goalCard = engine.RecommendNearestCrafts(goal.Id, inventory)
            .First(item => item.Route.GoalUnitId == goal.Id);
        var supportCard = engine.RecommendNearestCrafts(support.Id, inventory)
            .First(item => item.Route.GoalUnitId == support.Id);

        Assert.True(new RecipeCompletionCalculator(catalog.Unit)
            .Calculate([goal.Id], inventory.ToDictionary(x => x.UnitId, x => x.Count))
            .MissingLeaves.Count > 0);
        var result = RecommendationUrgencyPolicy.Apply(catalog, goal, inventory,
            [goalCard, supportCard], 30, 13, "신");

        Assert.Equal(support.Id, result.Recommendations[0].Route.GoalUnitId);
    }
}
