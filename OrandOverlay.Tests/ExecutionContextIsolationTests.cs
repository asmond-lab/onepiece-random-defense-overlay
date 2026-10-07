using Xunit;

namespace OrandOverlay.Tests;

public sealed class ExecutionContextIsolationTests
{
    private static string Source(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, name));
    }

    [Fact]
    public void CoachReview_MustReadJournalSeamNotPathAlone()
    {
        var source = Source("MainWindow.Coach.cs");
        Assert.Contains("CoachReview.Read(_coachJournal)", source);
        Assert.Contains("_coachJournal is not { HasLatest: true }", source);
        Assert.DoesNotContain("CoachReview.Read(path)", source);
        Assert.Contains("CoachJournal.Memory()", Source("OverlayExecutionContext.cs"));
        Assert.Contains("File.AppendAllTextAsync", Source("CoachJournal.cs"));
        Assert.DoesNotContain("new CoachJournal(", Source("OverlayExecutionContext.cs").Split("FixtureWithMemoryJournal")[1].Split("private static AppSettings Clone")[0]);
    }

    [Fact]
    public void UpdateCoordinator_MustRequireExecutionAuthority()
    {
        Assert.Contains("OverlayExecutionContext execution", Source("AppUpdateCoordinator.cs"));
        Assert.DoesNotContain("new UpdateService()", Source("AppUpdateCoordinator.cs"));
    }

    [Fact]
    public void UnitSuite_MustNotAutomaticallyRunLocalArchiveIntegration()
    {
        var source = Source("OrandOverlay.Tests/NavigationMechanicsProfileTests.cs");
        Assert.Contains("Requires separately approved local MPQ integration", source);
    }

    [Fact]
    public void NativeAndNetworkEntryPointsMustUseContextAuthority()
    {
        Assert.Contains("Execution.RefreshProfilesAsync()", Source("App.xaml.cs"));
        Assert.Contains("_execution.TryRuntime(() => RegisterHotKey", Source("MainWindow.xaml.cs"));
        Assert.Contains("_execution.RunRuntime(() => UnregisterHotKey", Source("MainWindow.xaml.cs"));
        Assert.DoesNotContain("new ClearSnapshotRefreshService()", Source("MainWindow.xaml.cs"));
        Assert.DoesNotContain("new UpdateService()", Source("MainWindow.xaml.cs"));
    }

    [Fact]
    public void LiveReader_CacheAndProfilesMustBeInstanceOwned()
    {
        Assert.DoesNotContain("private static string GrowthPointerCachePath", Source("WarcraftMemoryRecognitionService.cs"));
        Assert.DoesNotContain("var userPath = Path.Combine(AppPaths", Source("MemoryRecognitionProfiles.cs"));
    }

    [Fact]
    public void FixtureSettings_SaveAndReloadRemainInMemoryAndDetached()
    {
        var supplied = new AppSettings { GoalUnitId = "original" };
        var context = OverlayExecutionContext.Fixture(supplied);
        var edited = context.LoadSettings();
        edited.GoalUnitId = "fixture";
        context.SaveSettings(edited);
        edited.GoalUnitId = "later";
        Assert.Equal("fixture", context.LoadSettings().GoalUnitId);
        Assert.Equal("original", supplied.GoalUnitId);
        Assert.Null(context.CatalogOverride);
        Assert.Null(context.ClearCacheFile);
    }

    [Fact]
    public void WindowAndApp_RequireExplicitExecutionContext()
    {
        Assert.Contains("OverlayExecutionContext", Source("MainWindow.xaml.cs"));
        Assert.Contains("OverlayExecutionContext", Source("App.xaml.cs"));
        Assert.DoesNotContain("SettingsStore.Save(_settings)", Source("MainWindow.xaml.cs"));
        Assert.DoesNotContain("File.AppendAllLines", Source("MainWindow.xaml.cs"));
        Assert.DoesNotContain("new WarcraftMemoryRecognitionService", Source("MainWindow.xaml.cs"));
        Assert.DoesNotContain("ClearSnapshotRefreshService.CacheFile", Source("MainWindow.xaml.cs"));
    }

    [Fact]
    public void Catalog_DefaultLoadingMustNotResolveSharedOverrides()
    {
        var source = Source("DataCatalog.cs").Split("public static class AppPaths")[0];
        Assert.DoesNotContain("AppPaths.UserDataDirectory", source);
    }

    // Architecture RED: inspect source only; never evaluate the unsafe shared getter.
    [Fact]
    public void AppPaths_GettersMustNotCreateDirectories()
    {
        var source = Source("DataCatalog.cs").Split("public static class AppPaths")[1];
        Assert.DoesNotContain("Directory.CreateDirectory", source);
    }
}
