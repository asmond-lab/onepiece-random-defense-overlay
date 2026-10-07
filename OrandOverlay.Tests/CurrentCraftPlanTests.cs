using Xunit;

namespace OrandOverlay.Tests;

public sealed class CurrentCraftPlanTests
{
    [Fact]
    public void ActualCraftPlanDoesNotConsumeLastStunForAnIntermediate()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["stun"] = new UnitDefinition
        {
            Id = "stun", Name = "스턴 재료",
            OfficialAbilities = [new() { Name = "스턴", DisplayValue = "1.4" }]
        };
        units["boss"] = new UnitDefinition
        {
            Id = "boss", Name = "보잡 중간 조합", Recipe = new() { ["stun"] = 1 },
            OfficialAbilities = [new() { Name = "보스 잡기", DisplayValue = "가능" }],
            CombineCommands = ["fixture"]
        };
        units["goal"] = new UnitDefinition
        {
            Id = "goal", Name = "최종 목표", Recipe = new() { ["boss"] = 1, ["missing"] = 1 }
        };
        units["missing"] = new UnitDefinition { Id = "missing", Name = "없는 재료" };
        var inventory = new[] { new InventoryEntry { UnitId = "stun" } };
        var policy = new CurrentCraftPolicy(catalog.Unit, units["goal"],
            new Dictionary<string, int> { ["stun"] = 1 },
            new GoalStrategyProfile(1, 0), 30, 13, new RecipeCompletionCalculator(catalog.Unit));
        var recommendation = new Recommendation
        {
            Route = new RouteDefinition { Id = "craft:goal", GoalUnitId = "goal", Name = "최종 목표" },
            CurrentCraft = policy.Assess(units["goal"]),
            RemainingCraftSteps =
            [
                new RecipeCraftStep
                {
                    UnitId = "boss", Name = "보잡 중간 조합", RequiredCount = 1,
                    Ingredients = [new() { UnitId = "stun", Name = "스턴 재료", RequiredCount = 1 }]
                }
            ]
        };
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(
            AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        Assert.True(hotkeys.HasData);
        var planner = new AutoCombinePlanner(catalog, hotkeys);

        var steps = planner.Plan([recommendation], inventory);

        Assert.Empty(steps);
        Assert.Equal(1, inventory[0].Count);
    }
}
