using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GameplayStatsTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal static string Document => """
        {"schemaVersion":1,"generatedAt":"2026-09-09T00:00:00Z","totalRecords":40,"labeledRecords":40,
        "goals":{"goal":{"plays":40,"labeled":40,"clears":20,"adherenceMean":0.5,"failHeavyUnits":[]}},
        "weights":{},"goalWeights":{"goal":{"support":0.1}},"difficulties":{"신":40}}
        """;

    [Fact]
    public void UnboundLegacyWeightsCannotFeedAnEngine()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Document);
            var stats = LiveStats.Load(path);
            Assert.Equal(0, stats.WeightFor("missing-goal", "support"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":3}")]
    public void InvalidSchemaIsRejected(string json) =>
        Assert.False(LiveStats.TryParse(json, Hash, "신", out _));

    [Theory]
    [InlineData("weight")]
    [InlineData("counters")]
    [InlineData("difficulty")]
    [InlineData("extra")]
    [InlineData("gate")]
    public void InvalidStatisticsCannotReplaceACohort(string mutation)
    {
        var root = JsonNode.Parse(Document)!;
        switch (mutation)
        {
            case "weight": root["goalWeights"]!["goal"]!["support"] = 0.10001; break;
            case "counters": root["labeledRecords"] = 41; break;
            case "difficulty": root["difficulties"] = new JsonObject { ["easy"] = 40 }; break;
            case "extra": root["prose"] = "private"; break;
            case "gate": root["goals"]!["goal"]!["clears"] = 40; break;
        }
        Assert.False(LiveStats.TryParse(root.ToJsonString(), Hash, "신", out _));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("1e999")]
    [InlineData("-Infinity")]
    public void NonfiniteWeightsAreRejected(string token) =>
        Assert.False(LiveStats.TryParse(Document.Replace("0.1", token, StringComparison.Ordinal), Hash, "신", out _));

    [Fact]
    public void DuplicateSchemaFieldsAreRejected() =>
        Assert.False(LiveStats.TryParse(Document.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal), Hash, "신", out _));

    [Fact]
    public async Task RefreshFeedsActualEngineScoreAndOfflineKeepsSameCohortOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), ".gameplay-learning-artifacts", Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var engine = new RecommendationEngine(new DataCatalog());
        var service = new GameplayStatsRefreshService(http, root, () => true);
        try
        {
            handler.Payload = await GenerateStatistics(Path.Combine(root, "collector"));
            var goal = new UnitDefinition { Id = "goal", Name = "Goal", Rawcodes = ["A90H"] };
            var support = new UnitDefinition { Id = "support", Name = "Support", Rawcodes = ["Q80h"] };
            var score = typeof(RecommendationEngine).GetMethod("CommunityPriorityScore", BindingFlags.NonPublic | BindingFlags.Instance)!;
            int Score() => (int)score.Invoke(engine, [goal, support])!;
            Assert.Equal(40, Score());
            Assert.True(await service.RefreshAsync(Hash, "신", engine));
            Assert.Equal(44, Score());
            var snapshot = service.GetSnapshot(Hash, "신");
            engine = new RecommendationEngine(new DataCatalog());
            engine.SetGameplayCohort(Hash, "신");
            engine.SetLiveStats(snapshot);
            Assert.Equal(44, Score());
            Assert.False(service.GetSnapshot(new string('b', 64), "신").MatchesCohort(new string('b', 64), "신"));
            var cache = Path.Combine(root, Hash + "-신.json");
            var saved = await File.ReadAllTextAsync(cache);
            handler.Payload = Document.Replace("0.1", "0.101", StringComparison.Ordinal);
            Assert.False(await service.RefreshAsync(Hash, "신", engine));
            Assert.Equal(saved, await File.ReadAllTextAsync(cache));
            Assert.Equal(44, Score());
            handler.Offline = true;
            engine = new RecommendationEngine(new DataCatalog());
            service = new GameplayStatsRefreshService(http, root, () => true);
            Assert.Equal(40, Score());
            Assert.False(await service.RefreshAsync(Hash, "신", engine));
            Assert.Equal(44, Score());
            Assert.False(await service.RefreshAsync(new string('b', 64), "신", engine));
            Assert.Equal(40, Score());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NoAuthorizationMeansNoHttpOrCacheEffects()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => false);
        Assert.False(await service.RefreshAsync(Hash, "신", new RecommendationEngine(new DataCatalog())));
        Assert.Equal(0, handler.Calls);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task RevokedConsentDiscardsInFlightResponseWithoutCreatingCache()
    {
        var root = Path.Combine(Path.GetTempPath(), ".gameplay-learning-artifacts", Guid.NewGuid().ToString("N"));
        var allowed = true;
        var handler = new PendingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid") };
        var service = new GameplayStatsRefreshService(http, root, () => allowed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var refresh = service.RefreshAsync(Hash, "신", new RecommendationEngine(new DataCatalog()), timeout.Token);
        await handler.Received.Task.WaitAsync(timeout.Token);
        allowed = false;
        handler.Response.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Document) });
        Assert.False(await refresh);
        Assert.False(Directory.Exists(root));
    }

    private sealed class PendingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Received.SetResult();
            return Response.Task.WaitAsync(token);
        }
    }

    private static async Task<string> GenerateStatistics(string artifacts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OrandOverlay.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var start = new System.Diagnostics.ProcessStartInfo("python")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(directory.FullName, "ops", "gameplay-collector", "synthetic_fixture.py"));
        start.ArgumentList.Add(artifacts);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Payload { get; set; } = Document;
        public bool Offline { get; set; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (Offline) throw new HttpRequestException("offline fixture");
            Assert.Equal("/v3/live-stats", request.RequestUri!.AbsolutePath);
            Assert.Contains("mapScriptSha256=" + Hash, request.RequestUri.Query);
            Assert.Contains("difficulty=" + Uri.EscapeDataString("신"), request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Payload) });
        }
    }
}
