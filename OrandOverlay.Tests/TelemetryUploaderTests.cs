using System.Net;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class TelemetryUploaderTests
{
    [Fact]
    public void RecordContainsOnlyCoarseIdentifierFreeFields()
    {
        var record = Fixture();
        var json = JsonSerializer.Serialize(record);

        Assert.True(TelemetryPrivacyContract.IsSafe(record));
        Assert.Equal(2, record.SchemaVersion);
        foreach (var forbidden in new[]
                 {
                     "recordId", "anonId", "capturedAt", "session", "rawcode",
                     "unitId", "goalUnitId", "finalHand", "topRecommendations",
                     "warcraftVersion", "navigationMode", "buildVariant"
                 })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F-]{23}", json);
        Assert.DoesNotMatch("[12][0-9]{3}-[01][0-9]-[0-3][0-9]T", json);
    }

    [Fact]
    public void PrivacyContractRejectsUnknownValuesAndExtraFields()
    {
        var json = JsonSerializer.Serialize(Fixture()).TrimEnd('}') +
                   ",\"sessionId\":\"hidden\"}";

        Assert.False(TelemetryPrivacyContract.IsSafeJson(json));
        Assert.False(TelemetryPrivacyContract.IsSafe(new TelemetryRecord
        {
            AppVersion = "0.6.64",
            MapVersion = "2.314",
            Difficulty = "player-name",
            DamageLane = "physical",
            GoalTierFamily = "불멸",
            Surface = "top-navigation"
        }));
    }

    [Fact]
    public async Task SchemaPreflightWithoutApprovalSendsNoPayload()
    {
        using var scope = new QueueScope();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var uploader = new TelemetryUploader(
            "https://example.invalid/v2/aggregates", scope.Path, http);
        uploader.Enqueue(Fixture());

        await uploader.FlushPendingAsync();

        Assert.Equal(1, uploader.PendingCount);
        Assert.Single(handler.Methods);
        Assert.Equal(HttpMethod.Options, handler.Methods[0]);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task ApprovedV2EndpointReceivesPayloadAndDeletesQueue()
    {
        using var scope = new QueueScope();
        var handler = new StubHandler(request => Approved(request.Method == HttpMethod.Options
            ? HttpStatusCode.NoContent
            : HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var uploader = new TelemetryUploader(
            "https://example.invalid/v2/aggregates", scope.Path, http);
        uploader.Enqueue(Fixture());

        await uploader.FlushPendingAsync();

        Assert.Equal(new[] { HttpMethod.Options, HttpMethod.Post }, handler.Methods);
        Assert.Single(handler.Bodies);
        Assert.Contains("\"schemaVersion\":2", handler.Bodies[0]);
        Assert.Equal(0, uploader.PendingCount);
    }

    [Fact]
    public async Task RemoteAndLocalKillSwitchesSendNoPayloadAndDeleteOnOptOut()
    {
        using var scope = new QueueScope();
        var handler = new StubHandler(_ =>
        {
            var response = Approved(HttpStatusCode.NoContent);
            response.Headers.TryAddWithoutValidation(TelemetryUploader.EnabledHeader, "false");
            return response;
        });
        using var http = new HttpClient(handler);
        var uploader = new TelemetryUploader(
            "https://example.invalid/v2/aggregates", scope.Path, http);
        uploader.Enqueue(Fixture());

        await uploader.FlushPendingAsync();
        Assert.Equal(1, uploader.PendingCount);
        Assert.Single(handler.Methods);

        uploader.SetEnabled(false, deletePending: true);
        await uploader.FlushPendingAsync();
        Assert.Equal(0, uploader.PendingCount);
        Assert.Single(handler.Methods);
    }

    [Fact]
    public void PersistedOptOutDeletesQueueDuringStartup()
    {
        using var scope = new QueueScope();
        var enabled = new TelemetryUploader(
            "https://example.invalid/v2/aggregates", scope.Path);
        enabled.Enqueue(Fixture());
        Assert.Equal(1, enabled.PendingCount);

        var disabled = new TelemetryUploader(
            "https://example.invalid/v2/aggregates", scope.Path, enabled: false);

        Assert.Equal(0, disabled.PendingCount);
    }

    [Fact]
    public void LegacyCorruptAndTemporaryQueueFilesAreDiscarded()
    {
        using var scope = new QueueScope();
        File.WriteAllText(System.IO.Path.Combine(scope.Path, "old.json"),
            "{\"schemaVersion\":1,\"anonId\":\"old\"}");
        File.WriteAllText(System.IO.Path.Combine(scope.Path, "broken.v2.json"), "{");
        var injected = JsonSerializer.Serialize(Fixture()).TrimEnd('}') +
                       ",\"anonId\":\"leak\"}";
        File.WriteAllText(System.IO.Path.Combine(scope.Path, "injected.v2.json"),
            injected);
        File.WriteAllText(System.IO.Path.Combine(scope.Path, "write.tmp"), "partial");

        var uploader = new TelemetryUploader(
            "https://example.invalid/v2/aggregates", scope.Path);

        Assert.Equal(0, uploader.PendingCount);
        Assert.Empty(Directory.GetFiles(scope.Path));
    }

    private static TelemetryRecord Fixture() => MatchTelemetryRecorder.Build(
        "0.6.64", "2.314", "악몽", DamageLane.Physical, "불멸[물리]",
        RecommendationSurface.TopAndNavigation, RecommendationUrgency.BossSurvival,
        11, 2, new RecognitionTelemetryCounts(18, 2, 1, 0), "clear");

    private static HttpResponseMessage Approved(HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.TryAddWithoutValidation(TelemetryUploader.SchemaHeader, "2");
        response.Headers.TryAddWithoutValidation(TelemetryUploader.AcceptedHeader, "true");
        return response;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(request);
        }
    }

    private sealed class QueueScope : IDisposable
    {
        public QueueScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"orand-telemetry-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
