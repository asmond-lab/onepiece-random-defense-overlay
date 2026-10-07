using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class IntermediateCraftCommitmentTests
{
    [Fact]
    public void CraftedDrakeInvestmentKeepsNekomamushiAheadOfAnUnrelatedReadySupport()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        InventoryEntry[] inventory = [Entry("rawcode:690H"), Entry("rawcode:X90h"),
            Entry("rawcode:S00h"), Entry("rawcode:210h"), Entry("rawcode:Q00h")];
        var engine = new RecommendationEngine(catalog);

        var recommendations = engine.RecommendNearestCrafts("rawcode:690H", inventory,
            difficulty: "신", round: 20, completedStoryStage: 13,
            committedCraftUnitId: "rawcode:Z90h");

        Assert.Equal("rawcode:Z90h", recommendations[0].Route.GoalUnitId);
        Assert.Contains(recommendations[0].RecipeTree!.Children,
            child => child.UnitId == "rawcode:X90h" && child.OwnedCount == 1);
        Assert.All(recommendations, item =>
            Assert.Equal(ReadinessDamageType.Magic, item.CombatReadiness!.DamageType));
    }

    [Fact]
    public void ActionableRecipeSurvivesNewCardsRepeatedRanksAndCraftedProgressUntilCompletion()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var commitment = new IntermediateCraftCommitment(catalog);
        var frame = Frame();
        commitment.Record(frame, Craft());
        foreach (var inventory in new InventoryEntry[][]
                 { [Entry("rawcode:690H")], [Entry("rawcode:690H"), Entry("rawcode:X90h")],
                     [Entry("rawcode:690H"), Entry("rawcode:X90h"), Entry("rawcode:B20h")] })
        {
            Assert.Equal("rawcode:Z90h", commitment.TargetFor(frame.GoalId!, 1, frame.Mode, inventory));
            commitment.Record(frame with { Recommendations = [Recommendation("rawcode:130h")] }, Craft());
            Assert.Equal("rawcode:Z90h", commitment.TargetUnitId);
        }
        Assert.Equal("rawcode:Z90h", commitment.TargetFor(frame.GoalId!, 1, PlayMode.Normal, [Entry("rawcode:X90h")]));
        Assert.Null(commitment.TargetFor(frame.GoalId!, 1, frame.Mode, [Entry("rawcode:Z90h")]));
    }

    [Theory]
    [InlineData(CoachActionKind.Gather)]
    [InlineData(CoachActionKind.Reward)]
    [InlineData(CoachActionKind.Navigation)]
    [InlineData(CoachActionKind.Recognition)]
    public void RankedOrDeferredRecipesDoNotCreateACommitment(CoachActionKind kind)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var commitment = new IntermediateCraftCommitment(catalog);
        commitment.Record(Frame(), Craft() with { Kind = kind });
        Assert.Null(commitment.TargetUnitId);
    }

    [Theory]
    [InlineData("yamato_transcendent", 1, PlayMode.Beginner)]
    [InlineData("rawcode:690H", 2, PlayMode.Beginner)]
    [InlineData("rawcode:690H", 1, PlayMode.Manual)]
    public void ExplicitGoalModeAndNewMatchInvalidateTheIntermediateTarget(string goal, long generation, PlayMode mode)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var commitment = new IntermediateCraftCommitment(catalog);
        commitment.Record(Frame(), Craft());
        Assert.Null(commitment.TargetFor(goal, generation, mode, [Entry("rawcode:X90h")]));
    }

    [Fact]
    public void ReadyBossSurvivalSupportCanInterruptAnInvestedRecipe()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var goal = catalog.Unit("rawcode:690H");
        var strategy = GoalStrategyCalculator.StrategyProfileFor(goal)!.Value;
        var inventory = catalog.AllUnits.Where(unit => unit.Id != "rawcode:Z90h" &&
                TopGradePolicy.BaseTier(unit.Tier) == "전설" &&
                GoalStrategyCalculator.StrategyMetricsFor(unit).BossControl > 0)
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .Select(unit => (Unit: unit, Counts: unit.Recipe.Concat(new Dictionary<string, int>
                { [goal.Id] = 1, ["rawcode:X90h"] = 1 }).GroupBy(pair => pair.Key)
                .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value))))
            .First(fixture => new CurrentCraftPolicy(catalog.Unit, goal, fixture.Counts, strategy,
                30, 13, new RecipeCompletionCalculator(catalog.Unit)).Assess(fixture.Unit) is
                    { Priority: <= 1, LosesRequiredCombat: false })
            .Counts.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var recommendations = new RecommendationEngine(catalog).RecommendNearestCrafts("rawcode:690H", inventory,
            difficulty: "신", round: 30, completedStoryStage: 13, committedCraftUnitId: "rawcode:Z90h");

        Assert.NotEqual("rawcode:Z90h", recommendations[0].Route.GoalUnitId);
        Assert.True(recommendations[0].CurrentCraft is { Priority: <= 1, LosesRequiredCombat: false });
    }

    private static CoachFrame Frame() => new()
    {
        MatchGeneration = 1, Revision = 1, Round = 20, CompletedStoryStage = 13,
        IsCurrent = true, GoalId = "rawcode:690H",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("rawcode:690H", 1),
        Recommendations = [Recommendation("rawcode:Z90h")]
    };

    private static CoachDecision Craft() => new(CoachActionKind.Craft, "craft:rawcode:X90h", "", "", "", "", "")
    {
        TargetUnitId = "rawcode:X90h", CraftRecipe = new RecipeCraftStep { UnitId = "rawcode:X90h", Name = "Drake" }
    };

    private static Recommendation Recommendation(string id) => new()
    {
        Route = new RouteDefinition { Id = "craft:" + id, GoalUnitId = id, Name = id }
    };

    private static InventoryEntry Entry(string id) => new() { UnitId = id, Count = 1 };
}
