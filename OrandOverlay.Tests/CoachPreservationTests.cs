using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachPreservationTests
{
    [Fact]
    public void PreservesAllocatedNestedMaterialsAndOwnedSubtreesWithoutReservingSurplus()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["goal"] = Unit("goal", new() { ["branch"] = 2 });
        units["branch"] = Unit("branch", new() { ["leaf"] = 3 });
        units["leaf"] = Unit("leaf");
        units["extra"] = Unit("extra");
        var frame = Frame("goal", new() { ["branch"] = 1, ["leaf"] = 5, ["extra"] = 1 });

        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);

        Assert.Equal(new Dictionary<string, long> { ["branch"] = 1, ["leaf"] = 3 }.OrderBy(pair => pair.Key),
            decision.PreservedMaterialCounts.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void SelectedGoalsShareOneAllocationAndDuplicateTargetsDoNotReserveExtraCopies()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["goal"] = Unit("goal", new() { ["leaf"] = 2 });
        units["second"] = Unit("second", new() { ["leaf"] = 2 });
        units["leaf"] = Unit("leaf");
        var frame = Frame("goal", new() { ["leaf"] = 6 }) with
        {
            SelectedGoalIds = ["goal", "second"], CommittedCraftUnitId = "second",
            Recommendations = [Recommendation("second")]
        };

        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);

        Assert.Equal(4, Assert.Single(decision.PreservedMaterialCounts).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrakeRemainsPreservedForCommittedNekomamushiEvenDuringRewardHold(bool rewardHold)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var frame = Frame("rawcode:690H", new() { ["rawcode:690H"] = 1, ["rawcode:X90h"] = 1 }) with
        {
            CommittedCraftUnitId = "rawcode:Z90h",
            Recommendations = [Recommendation("rawcode:130h")],
            Story = rewardHold ? new StoryRewardSequenceDecision(RecommendationSequenceStage.StoryReward,
                StorySequenceAction.WaitForStoryReward, "", "", "", "", "", null, null, 0, false) : null
        };

        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);

        Assert.Equal(1, decision.PreservedMaterialCounts.GetValueOrDefault("rawcode:X90h"));
        Assert.Equal(1, decision.PreservedMaterialCounts.GetValueOrDefault("rawcode:690H"));
        Assert.Null(decision.CraftRecipe);
        Assert.Equal(rewardHold, decision.CraftDeferredForReward);
        if (rewardHold) Assert.Equal(CoachActionKind.Story, decision.Kind);
    }

    [Fact]
    public void CurrentRecipeIsPreservedBeforeItsFirstActionCreatesACommitment()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var frame = Frame("rawcode:690H", new() { ["rawcode:690H"] = 1, ["rawcode:X90h"] = 1 }) with
        { Recommendations = [Recommendation("rawcode:Z90h")] };

        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);

        Assert.Equal(1, decision.PreservedMaterialCounts.GetValueOrDefault("rawcode:X90h"));
    }

    [Fact]
    public void UnknownGoalAndFinishedMatchDoNotExposePreservationCounts()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var frame = Frame("rawcode:690H", new() { ["rawcode:690H"] = 1, ["rawcode:X90h"] = 1 }) with
        { CommittedCraftUnitId = "rawcode:Z90h" };
        var planner = new BeginnerCoachPlanner(catalog);

        Assert.Empty(planner.Decide(frame with { GoalId = null }).PreservedMaterialCounts);
        Assert.Empty(planner.Decide(frame with { Outcome = "clear" }).PreservedMaterialCounts);
    }

    private static CoachFrame Frame(string goalId, Dictionary<string, int> inventory) => new()
    {
        MatchGeneration = 1, Revision = 1, Round = 20, CompletedStoryStage = 13,
        Difficulty = "신", IsCurrent = true, GoalId = goalId, Inventory = inventory.ToImmutableDictionary()
    };

    private static UnitDefinition Unit(string id, Dictionary<string, int>? recipe = null) =>
        new() { Id = id, Name = id, Recipe = recipe ?? [] };

    private static Recommendation Recommendation(string id) => new()
    { Route = new RouteDefinition { Id = "craft:" + id, GoalUnitId = id, Name = id } };
}
