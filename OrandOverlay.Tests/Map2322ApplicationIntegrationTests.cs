using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322ApplicationIntegrationTests
{
    [Fact]
    public void SelectedCatalogProjectsActiveRecipesCommandsHotkeysAndSafeOrdinaryYujiroCraft()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.322");
        var bundle = Assert.IsType<Map2322DataBundle>(catalog.Bundle2322);
        Assert.Null(catalog.OfflineBundle);
        Assert.Equal(bundle.Fingerprint, catalog.SelectedDatasetFingerprint);
        Assert.Equal(265, bundle.Recipes.ProjectAll().Count);
        Assert.Equal(81, bundle.Commands.Count);
        Assert.Equal(162, bundle.Commands.Values.Sum(x => x.Count));
        Assert.Equal(156, bundle.Hotkeys.Count);
        Assert.Equal(new[] { "아카이누조합", "akainu" }, catalog.Unit("rawcode:Z30h").CombineCommands);
        var unit = catalog.Unit("rawcode:2C0h");
        Assert.Equal("한마 유지로", unit.Name);
        Assert.Equal(1, unit.Recipe["rawcode:T60h"]);
        Assert.Equal(7, unit.Recipe["rawcode:LUMBER"]);
        Assert.Contains("KING", unit.RecipeConditions!.UnresolvedSourceConditions);
        Assert.Contains("PICK", unit.RecipeConditions.UnresolvedSourceConditions);
        Assert.Empty(unit.CombineCommands);
        var inventory = new Dictionary<string, int> { ["rawcode:T60h"] = 1,
            ["rawcode:Y20h"] = 1, ["rawcode:Y30h"] = 1, ["rawcode:G60h"] = 1 };
        var plan = new NormalCraftPlanner(catalog).Build(unit.Id, inventory);
        var step = Assert.Single(plan.Steps);
        Assert.Equal(7, step.Ingredients.Count);
        Assert.False(step.IsMaterialReady);
        Assert.Null(step.CombineKey);
        Assert.Empty(step.CombineCommands);
        Assert.Equal(RecipeConditionStatus.Unknown, step.Conditions.Status);
        Assert.Equal(10000, plan.ResourceRequirements.Gold);
        Assert.Equal(7, plan.ResourceRequirements.Lumber);
        Assert.Equal(0, Assert.Single(step.Ingredients, x => x.UnitId == "PICK:A800").OwnedCount);
    }

    [Fact]
    public void NavigationComesFromSelected2322SourceNotLegacyFallback()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.322");
        var options = MapNavigationCatalog.Options(catalog.Bundle2322!);
        Assert.Equal(15, options.Count);
        Assert.Equal(catalog.Bundle2322!.Navigation.Options.Select(x => x.Id), options.Select(x => x.Id));
        foreach (var category in NavigationProfiles.Categories)
        {
            var menu = MapNavigationCatalog.ForCategory(catalog, category.Id);
            Assert.Equal("Unselected", menu[0].Id);
            Assert.Equal(4, menu.Count);
            Assert.All(menu.Skip(1), option => Assert.Equal(option, MapNavigationCatalog.Resolve(catalog, option.Id)));
        }
        Assert.Equal("Unselected", MapNavigationCatalog.Resolve(catalog, "Gambler.ContinuousBetting").Id);
        Assert.Equal("Unselected", MapNavigationCatalog.Resolve(catalog, null).Id);
    }

    [Fact]
    public void HistoricalDatasetsDoNotAcquireYujiro()
    {
        foreach (var version in new[] { "2.314", "2.320", "2.321" })
        {
            var catalog = new DataCatalog();
            catalog.Load(loadCarryPolicy: false, mapVersion: version);
            Assert.Null(catalog.Bundle2322);
            Assert.False(catalog.RawcodeCatalog.ContainsKey("2C0h"));
        }
    }
}
