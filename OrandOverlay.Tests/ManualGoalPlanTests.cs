using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ManualGoalPlanTests
{
    private static DataCatalog Catalog()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["leaf"] = new UnitDefinition { Id = "leaf", Name = "공유 재료" };
        foreach (var id in new[] { "a", "b", "c" })
            units[id] = new UnitDefinition
            {
                Id = id, Name = id, Tier = "초월 [물딜]",
                Recipe = new Dictionary<string, int> { ["leaf"] = 1 }
            };
        return catalog;
    }

    [Fact]
    public void TwoGoalsShareOneAllocationRatherThanBothClaimingTheSameCard()
    {
        var plan = ManualGoalPlan.Create(Catalog(), ["a", "b"],
            [new InventoryEntry { UnitId = "leaf" }], null);
        Assert.Equal(2, plan.Allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, plan.Allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, plan.Progress[0].Progress.OwnedLeafCount);
        Assert.Equal(0, plan.Progress[1].Progress.OwnedLeafCount);
    }

    [Fact]
    public void CompletedFirstGoalActivatesSecondAndIsReservedFromRecipes()
    {
        var plan = ManualGoalPlan.Create(Catalog(), ["a", "b"],
            [new InventoryEntry { UnitId = "a" }, new InventoryEntry { UnitId = "leaf" }],
            "AlliedForces.DoubleBenefit");
        Assert.Equal("b", plan.ActiveGoalId);
        Assert.Contains("a", plan.ProtectedGoalIds);
        Assert.Equal(0, plan.RecipeInventory["a"]);
    }

    [Fact]
    public void SecondGoalPlanningDoesNotWaitForNavigationConfirmation()
    {
        var plan = ManualGoalPlan.Create(Catalog(), ["a", "b"],
            [new InventoryEntry { UnitId = "a" }], null);
        Assert.Equal("b", plan.ActiveGoalId);
        Assert.False(plan.NavigationConflict);
    }

    [Fact]
    public void OccupiedOneTopSlotDoesNotAllowAnotherSelectedTop()
    {
        var plan = ManualGoalPlan.Create(Catalog(), ["a", "b"],
            [new InventoryEntry { UnitId = "c" }], "PathOfKings.BountyHunter");
        Assert.False(plan.CanCraftNewTop);
        Assert.True(plan.NavigationConflict);
    }

    [Fact]
    public void OneTopNavigationKeepsBothChoicesButDefersTheSecond()
    {
        var plan = ManualGoalPlan.Create(Catalog(), ["a", "b"],
            [new InventoryEntry { UnitId = "a" }], "PathOfKings.BountyHunter");
        Assert.True(plan.NavigationConflict);
        Assert.Equal(new[] { "a", "b" }, plan.GoalIds.ToArray());
        Assert.Equal("a", plan.ActiveGoalId);
    }

    [Fact]
    public void ThirdAndDuplicateGoalsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ManualGoalPlan.Create(Catalog(), ["a", "b", "c"], [], null));
        Assert.Throws<ArgumentException>(() => ManualGoalPlan.Create(Catalog(), ["a", "a"], [], null));
    }

    [Fact]
    public void ExplicitGoalBundleDrivesNavigationTopCount()
    {
        var catalog = Catalog();
        var source = new AdaptivePlanningInputSource
        {
            MatchGeneration = 0, RecognitionRevision = 1, Round = 20,
            Phase = PlannerPhase.Committed, Inventory = [], Units = catalog.UnitsById,
            GoalUnitId = "a", PlannedGoalUnitIds = ["a", "b"],
            NavigationOptionId = "Unselected", GoroseiMode = GoroseiMode.None,
            ManualLatches = new ManualLatches(true, false)
        };
        Assert.Equal(2, RouteQuestEvaluation.Evaluate(source).PlannedTopCount);
    }

    [Fact]
    public void NavigationBuildProgressUsesTheJointBundle()
    {
        var catalog = Catalog();
        var source = new AdaptivePlanningInputSource
        {
            MatchGeneration = 0, RecognitionRevision = 1, Round = 21,
            Phase = PlannerPhase.Committed,
            Inventory = [new InventoryEntry { UnitId = "leaf" }],
            Units = catalog.UnitsById, GoalUnitId = "a", PlannedGoalUnitIds = ["a", "b"],
            NavigationOptionId = "Unselected", GoroseiMode = GoroseiMode.None,
            ManualLatches = new ManualLatches(true, false)
        };
        var input = new AdaptivePlanningCoordinatorInputFactory(Path.Combine(AppContext.BaseDirectory, "Data"))
            .Create(source);
        Assert.Equal(5000, input.NavigationRequest.BeforeBuildBp);
        Assert.All(input.NavigationRequest.Options.Where(option =>
            NavigationProfiles.Find(option.OptionId).TopUnitLimit < 2), option => Assert.False(option.TopCompatible));
    }
}
