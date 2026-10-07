using Xunit;

namespace OrandOverlay.Tests;

public sealed class SelectedGoalProtectionTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public void CraftPlanReservesOneCopyOfAnOwnedSelectedGoal(int owned, int actions)
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["b"] = new UnitDefinition { Id = "b", Name = "보존 목표", Tier = "불멸" };
        units["a"] = new UnitDefinition
        {
            Id = "a", Name = "다음 목표", Tier = "초월", Recipe = new() { ["b"] = 1 },
            CombineCommands = ["fixture"]
        };
        var recommendation = new Recommendation
        {
            Route = new RouteDefinition { Id = "craft:a", GoalUnitId = "a", Name = "다음 목표" },
            RecipeTree = new RecipeTreeNode
            {
                UnitId = "a", Name = "다음 목표", RequiredCount = 1,
                Children = [new RecipeTreeNode { UnitId = "b", Name = "보존 목표", RequiredCount = 1 }]
            }
        };
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        Assert.True(hotkeys.HasData);
        var plan = new AutoCombinePlanner(catalog, hotkeys).Plan([recommendation],
            [new InventoryEntry { UnitId = "b", Count = owned }], ["b"], ["b"]);
        Assert.Equal(actions, plan.Count);
    }

    [Fact]
    public void OneConcreteSeraphimCannotSatisfyExactAndWildcardRequirements()
    {
        var units = new Dictionary<string, UnitDefinition>
        {
            ["a"] = new() { Id = "a", Name = "목표 A", Recipe = new() { [RecipeWildcards.AnySeraphim] = 1 } },
            ["b"] = new() { Id = "b", Name = "목표 B", Recipe = new() { ["seraphim"] = 1 } },
            ["seraphim"] = new() { Id = "seraphim", Name = "세라핌", Tier = "세라핌" },
            [RecipeWildcards.AnySeraphim] = new() { Id = RecipeWildcards.AnySeraphim, Name = "아무 세라핌" }
        };
        var allocation = new RecipeCompletionCalculator(id => units[id]).CalculateAllocation(
            ["a", "b"], new Dictionary<string, int> { ["seraphim"] = 1 });
        Assert.Equal(2, allocation.Progress.RequiredLeafCount);
        Assert.Equal(1, allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, allocation.ConsumedByUnitId["seraphim"]);
        Assert.Equal(0, allocation.RemainingInventory["seraphim"]);
    }
}
