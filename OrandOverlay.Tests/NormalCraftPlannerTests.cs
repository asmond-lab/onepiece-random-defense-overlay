using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalCraftPlannerTests
{
    [Fact]
    public void SharedSubrecipeIsAggregatedAndEachOwnedCardConsumedOnce()
    {
        var catalog = Fixture();
        var inventory = new Dictionary<string, int> { ["shared"] = 1, ["leaf"] = 3 };
        var plan = new NormalCraftPlanner(catalog).Build("goal", inventory);
        var shared = Assert.Single(plan.Steps, step => step.UnitId == "shared");
        Assert.Equal(2, shared.CombineCount);
        Assert.Equal(2, shared.OutputCount);
        var material = Assert.Single(shared.Ingredients);
        Assert.Equal(4, material.RequiredCount);
        Assert.Equal(3, material.OwnedCount);
        Assert.Equal(1, Assert.Single(plan.MissingMaterials).MissingCount);
        Assert.Equal(1, plan.Steps.SelectMany(step => step.Ingredients)
            .Where(item => item.UnitId == "shared").Sum(item => item.OwnedCount));
        Assert.Equal(2, plan.Steps.SelectMany(step => step.Ingredients)
            .Where(item => item.UnitId == "shared").Sum(item => item.PriorStepCount));
        Assert.Equal(3, inventory["leaf"]);
        Assert.Equal("shared", plan.Steps[0].UnitId);
        Assert.Equal("goal", plan.Steps[^1].UnitId);
    }

    [Fact]
    public void OwnedIntermediateSkipsItsEntireSubtreeAndRebuildUsesNewCounts()
    {
        var planner = new NormalCraftPlanner(Fixture());
        var inventory = new Dictionary<string, int> { ["left"] = 1, ["shared"] = 1 };
        var before = planner.Build("goal", inventory);
        Assert.Equal(new[] { "right", "goal" }, before.Steps.Select(step => step.UnitId));
        Assert.Empty(before.MissingMaterials);
        inventory["right"] = 1;
        inventory["shared"] = 0;
        var after = planner.Build("goal", inventory);
        Assert.Equal("goal", Assert.Single(after.Steps).UnitId);
        Assert.Equal(2, before.Steps.Count);
        inventory["goal"] = 1;
        var completed = planner.Build("goal", inventory);
        Assert.True(completed.GoalOwned);
        Assert.Empty(completed.Steps);
    }

    [Fact]
    public void GeneratedOutputIsConsumedOnlyOnceAcrossSiblingSteps()
    {
        var plan = new NormalCraftPlanner(Fixture()).Build("goal", new Dictionary<string, int> { ["leaf"] = 6 });
        var generated = new Dictionary<string, long>();
        foreach (var step in plan.Steps)
        {
            foreach (var item in step.Ingredients)
            {
                Assert.True(generated.GetValueOrDefault(item.UnitId) >= item.PriorStepCount);
                generated[item.UnitId] = generated.GetValueOrDefault(item.UnitId) - item.PriorStepCount;
            }
            generated[step.UnitId] = generated.GetValueOrDefault(step.UnitId) + step.OutputCount;
        }
        Assert.Equal(1, generated["goal"]);
        Assert.Equal(0, generated["shared"]);
        Assert.Equal(0, generated["left"]);
        Assert.Equal(0, generated["right"]);
        Assert.Empty(plan.MissingMaterials);
    }

    [Fact]
    public void UnknownCommandAndUnobservedConditionsAreExplicit()
    {
        var condition = new RecipeConditionRequirements("2.320", false, "token", 1, 5);
        var catalog = Fixture(Unit("conditional", new() { ["leaf"] = 1, ["rawcode:LUMBER"] = 5 }, condition));
        var plan = new NormalCraftPlanner(catalog).Build("conditional", new Dictionary<string, int> { ["leaf"] = 1 });
        var step = Assert.Single(plan.Steps);
        Assert.Null(step.CombineKey);
        Assert.Null(step.SelectionUnitId);
        Assert.Equal("선택 유닛 미확인", step.SelectionName);
        Assert.Empty(step.CombineCommands);
        Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
        Assert.Contains("조건·자원 확인 필요", step.StatusText);
        Assert.Equal(5, plan.ResourceRequirements.Lumber);
        Assert.Equal(5, Assert.Single(step.Ingredients, item => item.IsResource).RequiredCount);
    }

    [Fact]
    public void CyclesAndNegativeInventoryDoNotProduceFabricatedSteps()
    {
        var catalog = Fixture(Unit("cycle-a", new() { ["cycle-b"] = 1 }), Unit("cycle-b", new() { ["cycle-a"] = 1 }));
        var plan = new NormalCraftPlanner(catalog).Build("cycle-a", new Dictionary<string, int> { ["cycle-a"] = -2 });
        Assert.Empty(plan.Steps);
        Assert.Equal("cycle-a", Assert.Single(plan.MissingMaterials).UnitId);
        Assert.Contains("순환 조합", plan.Caveat);
    }

    [Fact]
    public void WildcardReservesExactCardAndDoesNotUseSyntheticCounts()
    {
        var catalog = Fixture(Unit("wild-goal", new() { ["sera-a"] = 1, [RecipeWildcards.AnySeraphim] = 1 }),
            Unit("sera-a", tier: "세라핌"), Unit("sera-b", tier: "세라핌"));
        var planner = new NormalCraftPlanner(catalog);
        var insufficient = planner.Build("wild-goal", new Dictionary<string, int>
            { ["sera-a"] = 1, [RecipeWildcards.AnySeraphim] = 20 });
        Assert.Equal(1, Assert.Single(insufficient.MissingMaterials).MissingCount);
        Assert.Equal(0, Assert.Single(Assert.Single(insufficient.Steps).Ingredients,
            item => item.UnitId == RecipeWildcards.AnySeraphim).OwnedCount);
        var sufficient = planner.Build("wild-goal", new Dictionary<string, int> { ["sera-a"] = 1, ["sera-b"] = 1 });
        Assert.Empty(sufficient.MissingMaterials);
        Assert.All(Assert.Single(sufficient.Steps).Ingredients, item => Assert.Equal(1, item.OwnedCount));
    }

    [Fact]
    public void YujiroChoiceCannotBeFabricatedByWildcardOrRandomInventory()
    {
        var inventory = new Dictionary<string, int>
        {
            ["T60h"] = 1, ["Y20h"] = 1, ["Y30h"] = 1, ["G60h"] = 1,
            ["RANDOM"] = 100, [RecipeWildcards.AnySeraphim] = 100
        };
        var plan = new NormalCraftPlanner(new DataCatalog()).Build2322Yujiro(inventory);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(0, Assert.Single(step.Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
        Assert.False(step.IsMaterialReady);
        Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
    }

    [Fact]
    public void Bundled2320CommandsAndOneUnitOutputsComeFromPinnedSource()
    {
        var catalog = Load2320();
        var unit = catalog.Unit("rawcode:V30h");
        var inventory = unit.Recipe.Where(pair => !pair.Key.Contains("LUMBER", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var plan = new NormalCraftPlanner(catalog).Build(unit.Id, inventory);
        var step = Assert.Single(plan.Steps, item => item.UnitId == unit.Id);
        Assert.Equal(new[] { "혁명군대리사범", "koala" }, step.CombineCommands);
        Assert.Null(step.CombineKey);
        Assert.Equal("채팅 조합 · 선택 조건 미확인", step.SelectionName);
        Assert.Equal(1, step.OutputCount);
        Assert.All(catalog.OfflineBundle!.Recipes.Document.Recipes,
            row => Assert.Equal(1, Assert.Single(row.Outputs).Count));
        Assert.Contains("source-sha256=" + Map2320RecipeRegistry.SourceSha256, catalog.OfflineBundle.Commands);
    }

    [Fact]
    public void Bundled2320MetadataDoesNotBecomeMaterialOrAFreeCraft()
    {
        var catalog = Load2320();
        var registry = catalog.OfflineBundle!.Recipes;
        var metadataOnly = registry.Project("X50h")!;
        Assert.Empty(metadataOnly.IngredientsByAppRawcode);
        Assert.NotEmpty(metadataOnly.UiMetadata);
        var plan = new NormalCraftPlanner(catalog).Build("rawcode:X50h", new Dictionary<string, int>());
        Assert.Empty(plan.Steps);
        Assert.Equal("rawcode:X50h", Assert.Single(plan.MissingMaterials).UnitId);
    }

    [Fact]
    public void LowestCraftTierPrecedesIndependentHigherBranches()
    {
        var catalog = Fixture(Unit("tier-goal", new() { ["left"] = 1, ["z-low"] = 1 }, tier: "초월"),
            Unit("z-low", new() { ["leaf"] = 1 }, tier: "안흔함"));
        var plan = new NormalCraftPlanner(catalog).Build("tier-goal", new Dictionary<string, int>());
        Assert.Equal(new[] { "shared", "z-low", "left", "tier-goal" }, plan.Steps.Select(step => step.UnitId));
    }

    [Fact]
    public void Exact2320HotkeysSelectVerifiedAbilityHostFromSourceIngredients()
    {
        var catalog = Load2320();
        var commands = NormalCraftCommandCatalog.LoadBundled(catalog.OfflineBundle!.Recipes);
        Assert.Equal(155, commands.Entries.Count);
        var goal = catalog.Unit("rawcode:K00h");
        var inventory = goal.Recipe.ToDictionary(pair => pair.Key, pair => pair.Value);
        var step = Assert.Single(new NormalCraftPlanner(catalog).Build(goal.Id, inventory).Steps);
        Assert.Equal("Z", step.CombineKey);
        Assert.NotNull(step.SelectionUnitId);
        Assert.Contains("300h", catalog.Unit(step.SelectionUnitId).Rawcodes);
        Assert.Contains(step.Ingredients, item => item.UnitId == step.SelectionUnitId);
        Assert.DoesNotContain("미확인", step.SelectionName);
    }

    [Fact]
    public void ChangedHotkeyDataAndWrongVersionFailClosed()
    {
        var catalog = Load2320();
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", NormalCraftCommandCatalog.DataFileName));
        bytes[^2] ^= 1;
        Assert.Throws<InvalidDataException>(() => NormalCraftCommandCatalog.Load(bytes, catalog.OfflineBundle!.Recipes));
        var legacy = new DataCatalog();
        legacy.Load(loadCarryPolicy: false);
        var goal = legacy.Unit("rawcode:K00h");
        var step = Assert.Single(new NormalCraftPlanner(legacy).Build(goal.Id, goal.Recipe).Steps);
        Assert.Null(step.CombineKey);
    }

    [Fact]
    public void ReusedPlannerDrops2320HotkeysWhenCatalogVersionChanges()
    {
        var catalog = Load2320();
        var planner = new NormalCraftPlanner(catalog);
        var current = catalog.Unit("rawcode:K00h");
        Assert.Equal("Z", Assert.Single(planner.Build(current.Id, current.Recipe).Steps).CombineKey);
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.314");
        var legacy = catalog.Unit("rawcode:K00h");
        var step = Assert.Single(planner.Build(legacy.Id, legacy.Recipe).Steps);
        Assert.Null(step.CombineKey);
        Assert.Empty(step.CombineCommands);
        Assert.Null(step.SelectionUnitId);
    }

    private static DataCatalog Load2320()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.320");
        return catalog;
    }

    private static DataCatalog Fixture(params UnitDefinition[] extra)
    {
        var file = Path.Combine(Path.GetTempPath(), "normal-craft-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new GameData { Units = [
                Unit("goal", new() { ["left"] = 1, ["right"] = 1 }),
                Unit("left", new() { ["shared"] = 2 }), Unit("right", new() { ["shared"] = 1 }),
                Unit("shared", new() { ["leaf"] = 2 }, tier: "안흔함"), Unit("leaf", tier: "흔함"), .. extra] }));
            var catalog = new DataCatalog(file);
            catalog.Load(loadCarryPolicy: false);
            return catalog;
        }
        finally { File.Delete(file); }
    }

    private static UnitDefinition Unit(string id, Dictionary<string, int>? recipe = null,
        RecipeConditionRequirements? conditions = null, string tier = "희귀함") =>
        new() { Id = id, Name = id, Tier = tier, Recipe = recipe ?? [], RecipeConditions = conditions };
}
