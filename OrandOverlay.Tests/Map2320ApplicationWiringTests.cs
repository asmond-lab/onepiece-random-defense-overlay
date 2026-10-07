using Xunit;

namespace OrandOverlay.Tests;

// Bounded source and offline-data checks only: never instantiate MainWindow or Production.
public sealed class Map2320ApplicationWiringTests
{
    private static DataCatalog Catalog(string version)
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: version);
        return catalog;
    }

    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void LiveCompositionUsesModernAndFixturesExplicitLegacy()
    {
        var source = Source("MainWindow.xaml.cs");
        Assert.Contains("string? fixtureMapVersion = null", source);
        Assert.Contains("_catalog.Load(mapVersion: fixtureMapVersion ?? (execution.LiveMemoryEnabled ? Map2323SourceContract.MapVersion : \"2.314\"));", source);
        const string overrideGuard = "if (fixtureMapVersion is not null && (execution.RuntimeEnabled || fixtureMapVersion is not (\"2.314\" or \"2.320\" or \"2.321\" or \"2.322\" or \"2.323\")))";
        const string rejection = "throw new ArgumentException(\"Map overrides are restricted to non-runtime fixtures.\", nameof(fixtureMapVersion));";
        Assert.Contains(overrideGuard, source);
        Assert.Contains(rejection, source);
        var guard = source.IndexOf(overrideGuard, StringComparison.Ordinal);
        var reject = source.IndexOf(rejection, guard, StringComparison.Ordinal);
        Assert.True(guard < reject);
        Assert.True(reject < source.IndexOf("_catalog = execution.CreateCatalog();", StringComparison.Ordinal));
        Assert.True(reject < source.IndexOf("InitializeComponent();", StringComparison.Ordinal));
        Assert.DoesNotContain("NavigationProfiles.Find(", source);
        Assert.Contains("var adaptiveWork = UsesMap2320 || playMode == PlayMode.Guide ? null :", source);
        Assert.Contains("if (startRuntime) InitializeGameplayServices();", source);
        var integration = Source("MainWindow.GameplayIntegration.cs");
        Assert.Contains("MapDatasetRuntimePolicy.AllowsLegacyRuntime(_catalog.MapVersion)", integration);
        Assert.Contains("_telemetry.SetEnabled(false, deletePending: false);", integration);
        Assert.Contains("if (!UsesMap2320) ConfigureGameplayEngine(nextEngine, recommendationDifficulty);", source);
        Assert.Contains("if (!UsesMap2320) _engine.SetLiveStats(_liveStats);", source);
        Assert.Contains("DataVersionText.Text = ApplicationDataVersionLabel();", source);
        Assert.Contains("DataVersionText.Visibility = Visibility.Visible;", source);
    }

    [Fact]
    public void ModernOptionsAreSourceBackedAndUnselectedInEveryCategory()
    {
        var catalog = Catalog("2.320");
        var options = MainWindow.ModernNavigationOptions(catalog.OfflineBundle!);
        Assert.Equal(15, options.Count);
        Assert.Equal(catalog.OfflineBundle!.Navigation.Options.Select(x => x.Id), options.Select(x => x.Id));
        Assert.Contains(options, x => x.Id == "Gambler.Exchange2320");
        Assert.DoesNotContain(options, x => x.Id == "Gambler.ContinuousBetting");
        Assert.Equal(0, options.Single(x => x.Id == "PathOfKings.MartialLaw").TopUnitLimit);
        Assert.All(options.Where(x => x.CategoryId == "PathOfKings" && x.Id != "PathOfKings.MartialLaw"), x => Assert.Equal(1, x.TopUnitLimit));
        Assert.All(options.Where(x => x.CategoryId != "PathOfKings"), x => Assert.Equal(int.MaxValue, x.TopUnitLimit));
        foreach (var option in options)
        {
            var display = MainWindow.ModernNavigationDisplay(option);
            Assert.Contains(option.Name, display);
            Assert.DoesNotContain(option.Summary, display);
            Assert.Equal(option.Id, MainWindow.ResolveVersionNavigation(catalog, option.Id).Id);
        }
        foreach (var category in NavigationProfiles.Categories)
        {
            var menu = MainWindow.VersionNavigationsForCategory(catalog, category.Id);
            Assert.Equal(4, menu.Count);
            Assert.Equal("Unselected", menu[0].Id);
            Assert.Equal(category.Id, menu[0].CategoryId);
        }
    }

    [Fact]
    public void ModernNavigationDisplayKeepsAllCurrentChoicesWithoutShowingMachineSummaries()
    {
        var catalog = Catalog("2.322");
        var options = MapNavigationCatalog.Options(catalog.Bundle2322!);
        Assert.Equal(15, options.Count);
        foreach (var option in options)
        {
            var display = MainWindow.ModernNavigationDisplay(option);
            Assert.Contains(option.Name, display);
            Assert.DoesNotContain(option.Summary, display);
            Assert.Equal(option.Id, MainWindow.ResolveVersionNavigation(catalog, option.Id).Id);
        }
        Assert.DoesNotContain(MapNavigationCatalog.Unselected().Summary,
            MainWindow.ModernNavigationDisplay(MapNavigationCatalog.Unselected()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unselected")]
    [InlineData("Gambler")]
    [InlineData("Gambler.ContinuousBetting")]
    [InlineData("unknown")]
    public void ModernUnknownNeverMigratesOrChoosesRealOption(string? id) =>
        Assert.Equal("Unselected", MainWindow.ResolveVersionNavigation(Catalog("2.320"), id).Id);

    [Fact]
    public void ExchangeIdentityAndLegacyOracleRemainSeparate()
    {
        Assert.Equal("Gambler.Exchange2320", MainWindow.ResolveVersionNavigation(Catalog("2.320"), "Gambler.Exchange2320").Id);
        var legacy = Catalog("2.314");
        Assert.Equal(NavigationProfiles.Find("Gambler"), MainWindow.ResolveVersionNavigation(legacy, "Gambler"));
        Assert.Equal(NavigationProfiles.ForCategory("Gambler"), MainWindow.VersionNavigationsForCategory(legacy, "Gambler"));
    }

    [Fact]
    public void ModernStoryIsGuaranteedProjectionAndStatsAreReferenceOnly()
    {
        var catalog = Catalog("2.320");
        var profile = MainWindow.LoadApplicationStoryProfile(catalog);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(catalog.OfflineBundle!.Story.ProjectGuaranteedBaseStages()),
            System.Text.Json.JsonSerializer.Serialize(profile.Stages));
        Assert.Null(MainWindow.RankingClearStats(catalog, ClearBuildStats.Empty));
        var label = MainWindow.VersionLabel(catalog, "legacy learned sample claim");
        Assert.Contains(catalog.MapVersion, label);
        Assert.DoesNotContain("legacy learned sample claim", label);
        var legacy = Catalog("2.314");
        Assert.Equal($"데이터 {legacy.Data.DataVersion} · {legacy.Data.Disclaimer}legacy", MainWindow.VersionLabel(legacy, "legacy"));
    }

    [Fact]
    public void ModernAdvisorNeverEnumeratesInventoryEvenInLegacySelectionWindow()
    {
        var advisor = new NavigationAdvisor(Catalog("2.320"));
        Assert.Empty(advisor.EvaluateGoals(null!, [], 1));
        Assert.Empty(advisor.EvaluateGoals(null!, [], 21, selectionWindow: true));
        var source = Source("MainWindow.MapData2320.cs");
        Assert.Contains("_settings.AutoRecommendNavigation = false;", source);
        Assert.Contains("AutoNavigationCheck.IsEnabled = false;", source);
        Assert.Contains("ToolTipService.SetShowOnDisabled", source);
    }
}
