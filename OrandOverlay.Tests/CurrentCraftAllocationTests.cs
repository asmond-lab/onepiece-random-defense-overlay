using Xunit;

namespace OrandOverlay.Tests;

public sealed class CurrentCraftAllocationTests
{
    [Fact]
    public void BossCraftCannotSpendTheOnlyRequiredStun()
    {
        var stun = Unit("stun", ability: "스턴", value: "1.4");
        var boss = Unit("boss", new() { ["stun"] = 1 }, "보스 잡기", "가능");
        var goal = Unit("goal", new() { ["missing"] = 1 });
        var units = new[] { stun, boss, goal, Unit("missing") }.ToDictionary(x => x.Id);
        var policy = Policy(units, goal, new() { ["stun"] = 1 });

        var action = policy.Assess(boss);

        Assert.True(action.MaterialsReady);
        Assert.True(action.LosesRequiredCombat);
        Assert.True(action.Priority > 3);
    }

    [Fact]
    public void GoalMaterialLossCountsReplacementCardsAfterCraft()
    {
        var goal = Unit("goal", new() { ["shared"] = 1, ["missing"] = 1 });
        var costly = Unit("costly", new() { ["shared"] = 1 }, "스턴", "1.4");
        var spare = Unit("spare", new() { ["free"] = 1 }, "스턴", "1.4");
        var units = new[] { goal, costly, spare, Unit("shared"), Unit("missing"), Unit("free") }
            .ToDictionary(x => x.Id);
        var inventory = new Dictionary<string, int> { ["shared"] = 1, ["free"] = 1 };
        var policy = Policy(units, goal, inventory);

        Assert.Equal(1, policy.Assess(costly).GoalMaterialLoss);
        Assert.Equal(0, policy.Assess(spare).GoalMaterialLoss);
        Assert.Equal(1, inventory["shared"]);
        Assert.Equal(1, inventory["free"]);
    }

    [Fact]
    public void CraftingAGoalIngredientPreservesItsAllocatedProgress()
    {
        var ingredient = Unit("ingredient", new() { ["shared"] = 1 }, "스턴", "1.4");
        var goal = Unit("goal", new() { ["ingredient"] = 1, ["missing"] = 1 });
        var units = new[] { goal, ingredient, Unit("shared"), Unit("missing") }.ToDictionary(x => x.Id);
        var policy = Policy(units, goal, new() { ["shared"] = 1 });

        Assert.Equal(0, policy.Assess(ingredient).GoalMaterialLoss);
    }

    [Fact]
    public void LeafCompletionDoesNotPretendAnUnbuiltIntermediateIsOnTheBoard()
    {
        var middle = Unit("middle", new() { ["leaf"] = 1 });
        var boss = Unit("boss", new() { ["middle"] = 1 }, "보스 잡기", "가능");
        var units = new[] { middle, boss, Unit("leaf") }.ToDictionary(x => x.Id);
        var policy = Policy(units, boss, new() { ["leaf"] = 1 });

        Assert.False(policy.Assess(boss).MaterialsReady);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(10, true, false)]
    [InlineData(null, true, true)]
    public void ResourceShortageAndUnknownBalanceAreNotConfused(int? lumber, bool ready, bool unknown)
    {
        var goal = Unit("goal", new() { ["leaf"] = 1, ["wood"] = 10 });
        var wood = new UnitDefinition { Id = "wood", Name = "wood", Tier = "자원", Rawcodes = ["LUMBER"] };
        var units = new[] { goal, wood, Unit("leaf") }.ToDictionary(x => x.Id);
        var inventory = new Dictionary<string, int> { ["leaf"] = 1 };
        if (lumber is { } amount) inventory["wood"] = amount;

        var action = Policy(units, goal, inventory).Assess(goal);

        Assert.Equal(ready, action.MaterialsReady);
        Assert.Equal(unknown, action.NeedsResourceConfirmation);
    }

    private static CurrentCraftPolicy Policy(Dictionary<string, UnitDefinition> units,
        UnitDefinition goal, Dictionary<string, int> inventory) =>
        new(id => units[id], goal, inventory,
            new GoalStrategyProfile(1, 0, SlowTarget: 0, ArmorReductionTarget: 0),
            30, 13, new RecipeCompletionCalculator(id => units[id]));

    private static UnitDefinition Unit(string id, Dictionary<string, int>? recipe = null,
        string? ability = null, string value = "0") => new()
    {
        Id = id,
        Name = id,
        Recipe = recipe ?? [],
        OfficialAbilities = ability is null ? [] :
            [new UnitAbilityDisplay { Name = ability, DisplayValue = value }]
    };
}
