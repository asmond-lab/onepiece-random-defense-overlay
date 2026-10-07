using Xunit;
namespace OrandOverlay.Tests;

public sealed class Map2320RuntimeBoundaryTests
{
    [Theory]
    [InlineData("2.314", false, false, true)]
    [InlineData("2.320", false, false, false)]
    [InlineData("2.320", false, true, false)]
    [InlineData("2.320", true, false, false)]
    [InlineData("2.320", true, true, true)]
    [InlineData("2.321", true, true, false)]
    public void DatasetDoesNotApproveNativeReader(string version, bool experimental, bool explicitVerification, bool allowed)
    {
        Assert.Equal(allowed, MapDatasetRuntimePolicy.AllowsReader(version, experimental, explicitVerification));
        Assert.Equal(version == "2.314", MapDatasetRuntimePolicy.AllowsLegacyRuntime(version));
    }
    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WarcraftMemoryRecognitionService.cs"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }
    [Fact]
    public void DatasetGatePrecedesNativeOpenAndDoesNotReplaceProfileGate()
    {
        var source = Source("WarcraftMemoryRecognitionService.cs");
        var datasetGate = source.IndexOf("MapDatasetRuntimePolicy.AllowsReader", StringComparison.Ordinal);
        Assert.True(datasetGate > source.IndexOf("if (!profile.Enabled", StringComparison.Ordinal));
        Assert.True(datasetGate > source.IndexOf("if (!actualHash.Equals", StringComparison.Ordinal));
        Assert.True(datasetGate < source.IndexOf("ReadOnlyProcessMemory.Open(process.Id)", StringComparison.Ordinal));
        Assert.Contains("_catalog.MapVersion != _selectedMapVersion", source);
    }
    [Fact]
    public void ModernDataCannotCreateLegacyRecordingOrLearning()
    {
        var execution = Source("OverlayExecutionContext.cs");
        Assert.Contains("!RuntimeEnabled || !MapDatasetRuntimePolicy.AllowsLegacyRuntime(catalog.MapVersion)", execution);
        var integration = Source("MainWindow.GameplayIntegration.cs");
        Assert.Contains("_telemetry.SetEnabled(false, deletePending: false);", integration);
        Assert.Contains("if (!MapDatasetRuntimePolicy.AllowsLegacyRuntime(_catalog.MapVersion)) return;", integration);
    }
    [Fact]
    public void ModernHandStatsRemainLabelledHistoricalReference()
    {
        var modern = new DataCatalog(); modern.Load(mapVersion: "2.320");
        var stats = new InventoryStatsCalculator(modern).Calculate([]);
        Assert.True(stats.IsLegacyReferenceForSelectedMap);
        Assert.Contains("2.320", stats.SourceLabel);
        Assert.StartsWith("reference-only", stats.ProfileStatus);
        var legacy = new DataCatalog(); legacy.Load();
        Assert.False(new InventoryStatsCalculator(legacy).Calculate([]).IsLegacyReferenceForSelectedMap);
    }
    [Fact]
    public void Selected2322HandStatsAreReferenceOnlyBeforeNormalReadinessEvaluation()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false, mapVersion: Map2322SourceContract.MapVersion);
        Assert.Null(catalog.OfflineBundle);
        Assert.NotNull(catalog.Bundle2322);
        var stats = new InventoryStatsCalculator(catalog).Calculate(
            [new InventoryEntry { UnitId = "rawcode:A50h", Count = 1 }]);
        Assert.Equal(41, stats.ArmorReduction); // Preserve the historical source number, not a 2.322 board total.
        Assert.True(stats.IsLegacyReferenceForSelectedMap); // StatsOverlayWindow.TargetsKnown requires !referenceOnly.
        Assert.StartsWith("reference-only: unverified for 2.322", stats.ProfileStatus);
    }

    [Fact]
    public void ModernCoachAndAutomaticFallbackCannotOverwriteManualPlan()
    {
        var catalog = new DataCatalog(); catalog.Load(mapVersion: "2.320");
        Assert.Null(AutomaticNavigationAdvisor.Select(catalog,
            [new InventoryEntry { UnitId = "luffy_common", Count = 1 }], [catalog.Unit("rawcode:C40h")], 21, 1, null));
        var coach = Source("MainWindow.Coach.cs");
        Assert.Contains("_automaticNavigation = UsesMap2320 ? null", coach);
        Assert.Contains("_coachCurrent = !UsesMap2320 &&", coach);
        Assert.Equal("Gambler.Exchange2320", MapNavigationCatalog.Resolve(catalog, "Gambler.Exchange2320").Id);
        Assert.True(MapNavigationCatalog.Resolve(catalog, "Gambler.Exchange2320").AllowsMultipleTopUnits);
        Assert.Equal("Unselected", MapNavigationCatalog.Resolve(catalog, "Gambler.ContinuousBetting").Id);
        Assert.Contains("MapNavigationCatalog.Resolve(catalog, navigationMode)", Source("RecommendationEngine.cs"));
    }
    [Fact]
    public void UserOverrideCannotRestoreUnapprovedModernCommands()
    {
        var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "game-data.demo.json")))!;
        var units = data["units"]!.AsArray();
        var dragon = units.FirstOrDefault(node => node?["id"]?.GetValue<string>() == "dragon_legend");
        if (dragon is null)
        {
            dragon = new System.Text.Json.Nodes.JsonObject { ["id"] = "dragon_legend", ["name"] = "드래곤", ["tier"] = "전설", ["rawcodes"] = new System.Text.Json.Nodes.JsonArray("W20h") };
            units.Add(dragon);
        }
        dragon["combineCommands"] = new System.Text.Json.Nodes.JsonArray("legacy-command");
        var file = Path.Combine(Path.GetTempPath(), "orand-2320-command-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, data.ToJsonString());
            var catalog = new DataCatalog(file); catalog.Load(loadCarryPolicy: false, mapVersion: "2.320");
            Assert.Empty(catalog.Unit("dragon_legend").CombineCommands);
            catalog.Load(loadCarryPolicy: false, mapVersion: "2.314");
            Assert.Contains("legacy-command", catalog.Unit("dragon_legend").CombineCommands);
        }
        finally { File.Delete(file); }
    }
}
