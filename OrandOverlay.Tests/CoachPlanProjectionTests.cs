using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CoachPlanProjectionTests
{
    private readonly DataCatalog _catalog = new();
    public CoachPlanProjectionTests()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["goal"] = new() { Id = "goal", Name = "goal", Recipe = new() { ["owned"] = 1, ["missing"] = 2 } };
        units["owned"] = new() { Id = "owned", Name = "owned" };
        units["missing"] = new() { Id = "missing", Name = "missing", Recipe = new() { ["leaf"] = 3 } };
        units["leaf"] = new() { Id = "leaf", Name = "leaf" };
    }

    [Fact]
    public void ComponentsRetainMissingBranchesAndAllocatedCounts()
    {
        var plan = CoachPlanProjection.Create(Decision(), Frame(), _catalog);
        Assert.Equal("goal", plan.GoalId);
        Assert.Equal(17, plan.Round);
        Assert.Equal(8, plan.Story);
        Assert.Equal(4, plan.SelectionWisps);
        Assert.Equal(2, plan.Components.Count);
        Assert.True(plan.Components.Single(item => item.UnitId == "owned").IsOwned);
        var missing = plan.Components.Single(item => item.UnitId == "missing");
        Assert.False(missing.IsOwned);
        Assert.Equal(2, missing.RequiredCount);
        Assert.Equal(1, missing.OwnedCount);
        Assert.Equal(1, missing.RemainingCount);
        Assert.Equal(3, Assert.Single(missing.Children).RequiredCount);
    }

    [Theory]
    [InlineData(false, false, CoachActionKind.Craft)]
    [InlineData(true, true, CoachActionKind.Craft)]
    [InlineData(true, false, CoachActionKind.Finished)]
    public void NoCurrentObservationMeansNoSuccessfulOwnershipOrNumbers(bool current, bool start, CoachActionKind kind)
    {
        var plan = CoachPlanProjection.Create(Decision() with { Id = start ? "start" : "craft", Kind = kind },
            Frame() with { IsCurrent = current }, _catalog);
        Assert.False(plan.IsCurrent);
        Assert.Null(plan.Round);
        Assert.Null(plan.Story);
        Assert.Null(plan.SelectionWisps);
        Assert.Null(plan.CraftProgress);
        Assert.All(plan.Components, item => { Assert.Null(item.OwnedCount); Assert.False(item.IsOwned); });
    }

    [Fact]
    public void ReceiptCountsAreNotRecomputedFromPreownedComponents()
    {
        var receipt = new ObservedCraftProgress("missing", 5, 2, true);
        var plan = CoachPlanProjection.Create(Decision() with { CraftProgress = receipt }, Frame(), _catalog);
        Assert.Same(receipt, plan.CraftProgress);
        Assert.NotNull(plan.CraftProgress);
        Assert.Equal(3, plan.CraftProgress.RemainingCount);
        Assert.True(plan.CraftProgress.AwaitingRecognition);
        Assert.Equal(1, plan.Components.Single(item => item.UnitId == "missing").OwnedCount);
    }

    [Fact]
    public void PausedFrameKeepsKnownComponentsButHidesExecutionProgress()
    {
        var plan = CoachPlanProjection.Create(Decision(), Frame() with { Paused = true }, _catalog);
        Assert.True(plan.IsCurrent);
        Assert.Null(plan.CraftProgress);
        Assert.True(plan.Components.Single(item => item.UnitId == "owned").IsOwned);
    }

    [Fact]
    public void RouteContainsOnlyActualTargetAncestors()
    {
        var plan = CoachPlanProjection.Create(Decision() with { TargetUnitId = "leaf" }, Frame(), _catalog);
        Assert.Equal(new[] { "leaf", "missing", "goal" }, plan.RouteUnitIds);
        Assert.Equal(new[] { "leaf", "missing" }, plan.MainPath.Select(item => item.UnitId));
        Assert.Equal(3, plan.MainPath[0].RequiredCount);
        Assert.Equal(2, plan.MainPath[0].OwnedCount);
        Assert.Equal(new[] { "owned", "missing" }, plan.Components.Select(item => item.UnitId));
        Assert.Empty(CoachPlanProjection.Create(Decision() with { TargetUnitId = "unrelated" }, Frame(), _catalog).RouteUnitIds);
        Assert.Empty(CoachPlanProjection.Create(Decision() with { TargetUnitId = "leaf" }, Frame() with { IsCurrent = false }, _catalog).RouteUnitIds);
    }

    [Fact]
    public void DisplayTitleLeavesDecisionAndExecutionFieldsUntouched()
    {
        var unit = new UnitDefinition { Id = "unit", Name = "unit", Tier = "희귀함" };
        const string sentinel = "ACTION_SENTINEL";
        var decision = Decision() with { TargetUnitId = unit.Id,
            Title = RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier) + sentinel };
        var original = decision with { };
        Assert.Equal(unit.Name + sentinel, CoachPresentation.ActionTitle(decision, unit));
        Assert.Equal(original, decision);
        Assert.Equal(decision.Title, CoachPresentation.ActionTitle(decision, null));
    }

    private static CoachDecision Decision() => new(CoachActionKind.Craft, "craft", "", "", "", "", "")
        { CraftProgress = new("missing", 2, 1, false) };
    private static CoachFrame Frame() => new()
    {
        MatchGeneration = 1, Revision = 1, Round = 17, CompletedStoryStage = 8,
        IsCurrent = true, GoalId = "goal",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("owned", 1).Add("missing", 1).Add("leaf", 2),
        RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 4)
    };
}
