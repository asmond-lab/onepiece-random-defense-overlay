using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletLearningProfileTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Document => GameplayStatsTests.Document.Replace("\"goal\"", "\"rawcode:180h\"")
        .Replace("\"support\"", "\"rawcode:U20h\"");

    [Theory]
    [InlineData(PlayMode.Guide, 1, true, "rawcode:180h")]
    [InlineData(PlayMode.Guide, 1, false, "rawcode:180h")]
    [InlineData(PlayMode.Guide, 2, true, "rawcode:930h")]
    [InlineData(PlayMode.Manual, 1, true, "rawcode:930h")]
    public void RecorderKeepsGuideGoalSeparateFromIntermediateTargetAndOldManualSelection(
        PlayMode mode, int guide, bool staleSelection, string expectedGoal)
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var recorder = new GameplaySessionRecorder(() => true, new("1.0", "2.314", Hash, "1"), catalog);
        recorder.Observe(new GameplayTelemetryObservation
        {
            MatchGeneration = 1, RecognitionRevision = 1, Round = 1, Mode = mode, GuideNumber = guide,
            Difficulty = "신", GoalUnitIds = [expectedGoal],
            Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1)
        });
        var frame = new CoachFrame
        {
            MatchGeneration = 1, RecognitionRevision = 1, Revision = 1, Round = 1,
            CompletedStoryStage = 0, Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1),
            IsCurrent = true, GuideVisible = true, Mode = mode, GuideNumber = guide, Difficulty = "신",
            GoalId = "rawcode:180h", SelectedGoalIds = staleSelection ? ["rawcode:930h"] : [],
            GuidePlan = new(BulletGuideStage.FirstLegend, "rawcode:530h", false)
        };
        recorder.Recommend(frame, new(CoachActionKind.Gather, "fixture", "", "", "", "", "")
            { TargetUnitId = "rawcode:530h" });
        recorder.Complete("fail", "mapSettlement");
        var events = recorder.DrainPackets().SelectMany(packet => packet.Events).ToArray();
        var recommendation = Assert.Single(events, e => e.Kind == "recommendation");
        Assert.Equal("rawcode:530h", recommendation.TargetUnitId);
        Assert.Equal(expectedGoal, Assert.Single(recommendation.GoalUnitIds!.Value));
        Assert.Equal(expectedGoal, Assert.Single(Assert.Single(events, e => e.Kind == "outcome").GoalUnitIds!.Value));
    }

    [Fact]
    public void ModeSwitchRecommendationDoesNotBorrowPreviousGuideObservationIdentity()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var recorder = new GameplaySessionRecorder(() => true, new("1.0", "2.314", Hash, "1"), catalog);
        recorder.Observe(new GameplayTelemetryObservation
        {
            MatchGeneration = 1, RecognitionRevision = 1, Round = 1, Mode = PlayMode.Guide, GuideNumber = 1,
            Difficulty = "신", GoalUnitIds = [BulletGuidePolicy.GoalId]
        });
        recorder.Recommend(new CoachFrame
        {
            MatchGeneration = 1, RecognitionRevision = 1, Revision = 1, Round = 1, CompletedStoryStage = 0,
            Inventory = ImmutableDictionary<string, int>.Empty, IsCurrent = true, GuideVisible = true,
            Mode = PlayMode.Normal, GuideNumber = 0, GoalId = BulletGuidePolicy.GoalId, Difficulty = "신"
        }, new(CoachActionKind.Gather, "fixture", "", "", "", "", ""));
        var recommendation = Assert.Single(recorder.DrainPackets().SelectMany(packet => packet.Events), e => e.Kind == "recommendation");
        Assert.Equal("Normal", recommendation.Mode);
        Assert.Equal(0, recommendation.GuideNumber);
    }

    [Fact]
    public void CompiledMainHooksRefreshAndConsumeOnlyBulletProfileBeforeCommitment()
    {
        List<string> Calls(string method) => (List<string>)typeof(BulletAbilityWiringTests)
            .GetMethod("Calls", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [typeof(MainWindow), method])!;
        var refresh = Calls("RefreshAll");
        Assert.Contains("MainWindow.get_CurrentBulletGuideLearning", refresh);
        Assert.True(refresh.IndexOf("MainWindow.get_CurrentBulletGuideLearning") < refresh.IndexOf("BulletGuidePolicy.Plan"));
        Assert.True(refresh.IndexOf("BulletGuidePolicy.Plan") < refresh.IndexOf("MainWindow.PrepareFirstLegend"));
        Assert.Contains("MainWindow.RequestBulletLearning", Calls("RefreshGameplayStatsAsync"));
        Assert.Contains("MainWindow.RequestBulletLearning", Calls("PrepareFirstLegend"));
        var snapshot = Calls("get_CurrentBulletGuideLearning");
        Assert.Contains("GameplayStatsRefreshService.GetBulletSnapshot", snapshot);
        Assert.Contains("BulletGuideLearningBridge.FromSnapshot", snapshot);
        Assert.DoesNotContain("GameplayStatsRefreshService.GetSnapshot", snapshot);
        Assert.Contains("GameplayStatsRefreshService.RefreshBulletAsync", Calls("RefreshBulletLearningAsync"));
    }

    [Fact]
    public async Task ProfileRequestAndCacheFeedActualGuideButNeverGenericSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => true);
        try
        {
            Assert.True(await service.RefreshBulletAsync(Hash, "신"));
            var snapshot = service.GetBulletSnapshot(Hash, "신");
            Assert.Equal(LiveStats.BulletProfile, snapshot.Profile);
            Assert.False(snapshot.MatchesCohort(Hash, "신"));
            Assert.False(service.GetSnapshot(Hash, "신").MatchesCohort(Hash, "신"));
            var learning = BulletGuideLearningBridge.FromSnapshot(snapshot, Hash, "신");
            Assert.NotNull(learning);
            var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
            Assert.Equal("rawcode:U20h", new BulletGuidePolicy(catalog).Plan(10, 4,
                new Dictionary<string, int>(), "신", learning: learning).TargetUnitId);
            handler.Offline = true;
            service = new GameplayStatsRefreshService(http, root, () => true);
            Assert.False(await service.RefreshBulletAsync(Hash, "신"));
            Assert.NotNull(BulletGuideLearningBridge.FromSnapshot(service.GetBulletSnapshot(Hash, "신"), Hash, "신"));
            Assert.Null(BulletGuideLearningBridge.FromSnapshot(service.GetBulletSnapshot(new string('b', 64), "신"), new string('b', 64), "신"));
            Assert.False(File.Exists(Path.Combine(root, Hash + "-신.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("generic")]
    public async Task MissingOrWrongResponseProfileCannotBeRelabeled(string? profile)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new Handler { Profile = profile }) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => true);
        Assert.False(await service.RefreshBulletAsync(Hash, "신"));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task ProfileResponseCannotEnterGenericRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new Handler { RequestProfile = false }) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => true);
        Assert.False(await service.RefreshAsync(Hash, "신", new RecommendationEngine(new DataCatalog())));
        Assert.False(service.GetSnapshot(Hash, "신").MatchesCohort(Hash, "신"));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void GenericBoundSnapshotCannotBecomeGuideLearning()
    {
        Assert.True(LiveStats.TryParse(Document, Hash, "신", out var generic));
        Assert.Null(BulletGuideLearningBridge.FromSnapshot(generic, Hash, "신"));
        Assert.False(LiveStats.TryParse(GameplayStatsTests.Document, Hash, "신", out _, LiveStats.BulletProfile));
    }

    [Fact]
    public async Task NoConsentMeansNoProfileRequestOrCacheRead()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => false);
        Assert.False(await service.RefreshBulletAsync(Hash, "신"));
        Assert.Equal(0, handler.Calls);
        Assert.Null(BulletGuideLearningBridge.FromSnapshot(service.GetBulletSnapshot(Hash, "신"), Hash, "신"));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("mapScriptSha256")]
    [InlineData("difficulty")]
    [InlineData("schemaVersion")]
    public async Task OfflineProfileCacheMustCarryItsOwnExactBinding(string field)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => true);
        try
        {
            Assert.True(await service.RefreshBulletAsync(Hash, "신"));
            var path = Path.Combine(root, Hash + "-신-bullet-guide-1.json");
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            if (field == "schemaVersion") envelope[field] = 2;
            else if (field == "profile") envelope.AsObject().Remove(field);
            else envelope[field] = field == "difficulty" ? "악몽" : new string('b', 64);
            await File.WriteAllTextAsync(path, envelope.ToJsonString());
            handler.Offline = true;
            service = new GameplayStatsRefreshService(http, root, () => true);
            Assert.False(await service.RefreshBulletAsync(Hash, "신"));
            Assert.Null(BulletGuideLearningBridge.FromSnapshot(service.GetBulletSnapshot(Hash, "신"), Hash, "신"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RevocationDiscardsInflightProfileWithoutCacheEffects()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var allowed = true;
        var handler = new PendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => allowed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var refresh = service.RefreshBulletAsync(Hash, "신", timeout.Token);
        await handler.Received.Task.WaitAsync(timeout.Token);
        allowed = false;
        handler.Response.SetResult(Response());
        Assert.False(await refresh);
        Assert.False(Directory.Exists(root));
    }

    private static HttpResponseMessage Response(string? profile = "bullet-guide-1")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Document) };
        if (profile is not null) response.Headers.Add("X-Orand-Stats-Profile", profile);
        return response;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Offline { get; set; }
        public int Calls { get; private set; }
        public string? Profile { get; init; } = "bullet-guide-1";
        public bool RequestProfile { get; init; } = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Equal("?mapScriptSha256=" + Hash + "&difficulty=" + Uri.EscapeDataString("신") +
                (RequestProfile ? "&profile=bullet-guide-1" : ""), request.RequestUri!.Query);
            if (Offline) throw new HttpRequestException("offline fixture");
            return Task.FromResult(Response(Profile));
        }
    }
    private sealed class PendingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Received.SetResult(); return Response.Task.WaitAsync(token); }
    }
}
