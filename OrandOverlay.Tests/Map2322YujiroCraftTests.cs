using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322YujiroCraftTests
{
    private static readonly IReadOnlyDictionary<string, int> Fixed = new Dictionary<string, int>
    {
        ["T60h"] = 1, ["Y20h"] = 1, ["Y30h"] = 1, ["G60h"] = 1
    };

    [Fact]
    public void SourceCatalogMatchesRegisteredRecipeAndShowsAllTerms()
    {
        using var document = JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(
            AppContext.BaseDirectory, "Data", "map-unit-additions-2322.json")));
        var row = Assert.Single(document.RootElement.GetProperty("units").EnumerateArray());
        var projection = Map2322DataBundle.LoadBundled().Recipes.Project("2C0h")!;
        Assert.Equal("h0C2", row.GetProperty("sourceRawcode").GetString());
        Assert.Equal(projection.RecipeId, row.GetProperty("recipeId").GetString());
        Assert.Equal("A0QG", projection.RecipeId);
        Assert.Equal(new[] { "KING:h0C2", "PICK:A800" }, projection.ConditionalRequirements
            .Select(x => x.Source.Kind + ":" + x.Source.Id));
        Assert.Equal(new[] { "UNIT:h06T:T60h:1", "UNIT:h02Y:Y20h:1", "UNIT:h03Y:Y30h:1",
            "UNIT:h06G:G60h:1", "GOLD:GOLD:10000", "WOOD:LUMBER:7" },
            row.GetProperty("ingredients").EnumerateArray().Select(x => x.GetProperty("kind").GetString() + ":" +
                (x.TryGetProperty("sourceId", out var source) ? source.GetString() + ":" : "") +
                x.GetProperty("id").GetString() + ":" + x.GetProperty("count").GetInt32()));
        Assert.True(row.GetProperty("conditions")[1].GetProperty("consumesSelectedInstance").GetBoolean());
    }

    [Fact]
    public void FixedMaterialsAloneNeverMakeYujiroReady()
    {
        var step = Assert.Single(new NormalCraftPlanner(new DataCatalog()).Build2322Yujiro(Fixed).Steps);
        Assert.Equal(new[] { "T60h", "Y20h", "Y30h", "G60h", "GOLD", "LUMBER", "A800" },
            step.Ingredients.Select(x => x.UnitId.Replace("rawcode:", "").Replace("PICK:", "")));
        Assert.Equal(10000, Assert.Single(step.Ingredients, x => x.UnitId == "rawcode:GOLD").RequiredCount);
        Assert.Equal(7, Assert.Single(step.Ingredients, x => x.UnitId == "rawcode:LUMBER").RequiredCount);
        Assert.False(step.IsMaterialReady);
        Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
        Assert.Contains("KING:h0C2", step.Conditions.Reason);
        Assert.Contains("PICK:A800", step.Conditions.Reason);
        Assert.Equal(0, Assert.Single(step.Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
    }

    [Fact]
    public void Ordinary2322CatalogIdAndRawcodeAliasesCountOnePhysicalFixedUnit()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.322");
        Assert.Contains("Y30h", catalog.Unit("ivankov_hidden").Rawcodes);
        var planner = new NormalCraftPlanner(catalog);
        var inventory = new Dictionary<string, int>
        {
            ["rawcode:T60h"] = 1, ["rawcode:Y20h"] = 1,
            ["ivankov_hidden"] = 1, ["rawcode:G60h"] = 1
        };
        var canonical = planner.Build("rawcode:2C0h", inventory);
        var step = Assert.Single(canonical.Steps);
        Assert.Equal(7, step.Ingredients.Count);
        Assert.All(step.Ingredients.Where(x => x.UnitId is "rawcode:T60h" or "rawcode:Y20h" or
            "rawcode:Y30h" or "rawcode:G60h"), x => Assert.Equal(1, x.OwnedCount));
        Assert.Equal(1, Assert.Single(step.Ingredients, x => x.UnitId == "rawcode:Y30h").OwnedCount);
        Assert.Equal(0, Assert.Single(step.Ingredients, x => x.UnitId == "rawcode:Y30h").MissingCount);
        Assert.Equal(0, Assert.Single(step.Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
        Assert.Equal(10000, canonical.ResourceRequirements.Gold);
        Assert.Equal(7, canonical.ResourceRequirements.Lumber);
        Assert.False(step.IsMaterialReady);
        Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
        Assert.Contains("KING:h0C2", step.Conditions.Reason);
        Assert.Contains("PICK:A800", step.Conditions.Reason);
        Assert.Null(step.CombineKey);

        inventory["rawcode:Y30h"] = 1; // Another name for the same observed unit, not another unit.
        inventory["Y30h"] = 1;
        inventory.Remove("rawcode:Y20h");
        var aliased = planner.Build2322Yujiro(inventory, [new("ivankov-unit", "Y30h", true)]);
        var aliasedStep = Assert.Single(aliased.Steps);
        Assert.Equal(1, Assert.Single(aliasedStep.Ingredients, x => x.UnitId == "rawcode:Y30h").OwnedCount);
        Assert.Equal(0, Assert.Single(aliasedStep.Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
        Assert.Equal(0, Assert.Single(aliasedStep.Ingredients, x => x.UnitId == "rawcode:Y20h").OwnedCount);
        Assert.Equal(1, Assert.Single(aliasedStep.Ingredients, x => x.UnitId == "rawcode:Y20h").MissingCount);
        Assert.Equal(new[] { "rawcode:Y20h", "PICK:A800" }, aliased.MissingMaterials.Select(x => x.UnitId));
        Assert.False(aliasedStep.IsMaterialReady);
    }

    [Fact]
    public void TargetRequiresObservedAbilityAndDistinctUnreservedInstance()
    {
        var planner = new NormalCraftPlanner(new DataCatalog());
        var fixedOnly = planner.Build2322Yujiro(Fixed, [new("paimon", "T60h", true)]);
        Assert.Equal(0, Assert.Single(Assert.Single(fixedOnly.Steps).Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
        var inventory = new Dictionary<string, int>(Fixed) { ["T60h"] = 2 };
        var unknown = planner.Build2322Yujiro(inventory, [new("a-fixed", "T60h", true), new("z-choice", "T60h", false)]);
        Assert.Equal(0, Assert.Single(Assert.Single(unknown.Steps).Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
        var observed = planner.Build2322Yujiro(inventory, [new("a-fixed", "T60h", true), new("z-choice", "T60h", true)]);
        var step = Assert.Single(observed.Steps);
        Assert.Equal(1, Assert.Single(step.Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
        Assert.Contains("z-choice", step.Conditions.Reason);
        Assert.False(step.IsMaterialReady);
        Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
        Assert.Null(step.CombineKey);
        Assert.Equal(10000, observed.ResourceRequirements.Gold);
        Assert.Equal(7, observed.ResourceRequirements.Lumber);
    }
}
