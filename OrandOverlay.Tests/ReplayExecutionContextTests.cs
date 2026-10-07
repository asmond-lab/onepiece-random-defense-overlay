using System.Collections.Immutable;
using System.Reflection;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ReplayExecutionContextTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "replay-test-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_parent, ".replay-analysis-artifacts");

    private OverlayExecutionContext Create() => OverlayExecutionContext.Replay(Root,
        new AppSettings { Mode = PlayMode.Guide, GuideNumber = 1, TelemetryEnabled = true });

    [Fact]
    public async Task ReplayFactoriesRetainOnlyNativeReaderAndOwnedJournal()
    {
        var context = Create();
        Assert.False(context.RuntimeEnabled);
        Assert.True(context.LiveMemoryEnabled);
        Assert.False(OverlayExecutionContext.Fixture(new AppSettings()).LiveMemoryEnabled);
        Assert.False(context.HasCurrentConsent);
        Assert.False(context.EnsureConsent(() => throw new InvalidOperationException("Consent must not be requested")));
        context.RequireConsent();
        context.RunRuntime(() => throw new InvalidOperationException("Runtime capability leaked"));
        Assert.False(context.TryRuntime(() => throw new InvalidOperationException("Runtime capability leaked")));
        Assert.Null(context.CatalogOverride);
        Assert.Null(context.ClearCacheFile);
        Assert.Single(context.ClearSamplePaths());
        var catalog = context.CreateCatalog();
        catalog.Load(loadCarryPolicy: false);
        var reader = Assert.IsType<WarcraftMemoryRecognitionService>(context.CreateRecognizer(catalog));
        var cache = Assert.IsType<string>(typeof(WarcraftMemoryRecognitionService)
            .GetField("GrowthPointerCachePath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader));
        Assert.Equal(Path.Combine(context.ReplayDirectory!, "growth-unit-pointers.json"), cache);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.RecognizeAsync(context.LoadSettings(), cancelled.Token));
        var journal = context.CreateCoachJournal();
        Assert.NotNull(journal);
        Assert.True(journal.IsPersistent);
        Assert.True(await journal.RecordAsync(new CoachFrame { MatchGeneration = 1, Revision = 1, Round = 20, CompletedStoryStage = 0,
            Inventory = ImmutableDictionary<string, int>.Empty, IsCurrent = true },
            new CoachDecision(CoachActionKind.Waiting, "replay-test", "", "", "", "", "")));
        await journal.FlushAsync();
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, journal.LatestPath!);
        Assert.True(File.Exists(journal.LatestPath));
        Assert.NotEqual(journal, Create().CreateCoachJournal());
    }

    [Fact]
    public async Task ReplayRejectsNetworkAndPendingStorageEvenWhenEnabled()
    {
        var context = Create();
        Directory.CreateDirectory(Path.Combine(_parent, "pending"));
        var sentinel = Path.Combine(_parent, "pending", "sentinel.v2.json");
        await File.WriteAllTextAsync(sentinel, "retain");
        using var handler = new RejectNetwork();
        using var http = new HttpClient(handler);
        var metadata = new GameplayTelemetryMetadata("0.6.70", "2.314", RouteQuestCatalog.MapScriptSha256, "2.0.4.23745");
        Assert.Null(context.CreateGameplayTelemetry(metadata, context.CreateCatalog(), http));
        Assert.Null(context.CreateGameplayStatsRefreshService(http));
        Assert.Null(context.CreateUpdateService());
        Assert.Null(context.CreateClearRefreshService());
        await context.RefreshProfilesAsync();
        var telemetry = context.CreateTelemetry(true, Path.GetDirectoryName(sentinel));
        Assert.Null(typeof(TelemetryUploader).GetField("_http", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(telemetry));
        telemetry.SetEnabled(true);
        await telemetry.FlushPendingAsync();
        telemetry.DeletePending();
        telemetry.TrimQueue();
        Assert.False(telemetry.Enabled);
        Assert.Equal("retain", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void ReplaySettingsAreInMemoryAndSessionsHaveIndependentDirectories()
    {
        var first = Create();
        first.SaveSettings(new AppSettings { GoalUnitId = "replay-only" });
        Assert.Equal("replay-only", first.LoadSettings().GoalUnitId);
        Assert.NotEqual("replay-only", Create().LoadSettings().GoalUnitId);
        Assert.Equal(2, Directory.GetDirectories(Root).Length);
        Assert.Empty(Directory.GetFiles(Root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void ReplayRejectsNonAnalysisRootsBeforeCreatingFiles()
    {
        Assert.Throws<ArgumentException>(() => OverlayExecutionContext.Replay(_parent, new AppSettings()));
        Assert.Throws<ArgumentException>(() => OverlayExecutionContext.Replay(".replay-analysis-artifacts", new AppSettings()));
        Assert.False(Directory.Exists(_parent));
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Replay attempted HTTP: " + request.RequestUri);
        }
    }

    public void Dispose() { if (Directory.Exists(_parent)) Directory.Delete(_parent, true); }
}
