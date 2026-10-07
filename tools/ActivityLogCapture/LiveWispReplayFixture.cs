using System.Collections.Immutable;
using System.Text.Json;
using OrandOverlay;

internal sealed record LiveWispReplayEvidence(
    bool Success,
    bool RejectedBasicRetained,
    string? GapLane,
    bool? GapCorrelationReset,
    bool GameCounterReset,
    int GameWispCount,
    int WispCount,
    int CounterIncrement,
    int Candidate900h,
    bool DuplicateAction,
    bool ActionConfirmed,
    bool OutputsAttributed);

internal static class LiveWispReplayFixture
{
    private static readonly string Context = new('A', 64);
    private static readonly string World = new('B', 64);
    private const string Binding = "DC297D48F7F68E3F73BBF6117D2BB79D718C73011FD5014A6A084E8CDFCF6354";

    internal static IEnumerable<IAsyncEnumerable<DiagnosticRecognitionFrame>> CreateScans(DataCatalog catalog)
    {
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        yield return FullScan(catalog, 3438, 7000,
            start, Raw(marine: 1, xi: 2), f: 13);
        yield return RejectedBasicThenFullScan(catalog,
            start.AddMilliseconds(209), start.AddMilliseconds(250));
        yield return FullScan(catalog, 3440, 7002,
            start.AddMilliseconds(509), Raw(marine: 2, xi: 2), f: 14);
        yield return FullScan(catalog, 3441, 7003,
            start.AddMilliseconds(766), Raw(marine: 2, xi: 1), f: 14);
    }

    internal static LiveWispReplayEvidence Evaluate(IReadOnlyList<JsonElement> rows)
    {
        var rejectedBasicRetained = rows.Count(row =>
            Kind(row) == "memory.read" &&
            row.GetProperty("data").GetProperty("lane").GetString() == "basic" &&
            !row.GetProperty("data").GetProperty("accepted").GetBoolean() &&
            row.GetProperty("data").GetProperty("detail").GetString() ==
            "Diagnostic CUnit allocated handle generation changed") == 1;
        var gaps = rows.Where(row => Kind(row) == "observation.gap").ToArray();
        var gapData = gaps.Length == 1 ? gaps[0].GetProperty("data") : default;
        var gapLane = gapData.ValueKind == JsonValueKind.Object &&
                      gapData.TryGetProperty("lane", out var lane)
            ? lane.GetString()
            : null;
        var gapCorrelationReset = gapData.ValueKind == JsonValueKind.Object &&
                                  gapData.TryGetProperty("correlationReset", out var reset)
            ? (bool?)reset.GetBoolean()
            : null;
        var gameCounterReset = rows.Any(row => Kind(row) == "game.counter-reset");
        var wisps = rows.Where(row => Kind(row) == "game.wisp").ToArray();
        var wispData = wisps.Length == 1 ? wisps[0].GetProperty("data") : default;
        var wispCount = Integer(wispData, "count");
        var counterIncrement = Integer(wispData, "counterIncrement");
        var candidate900h = wispData.ValueKind == JsonValueKind.Object &&
                            wispData.TryGetProperty("candidateOutputs", out var candidates)
            ? Integer(candidates, "900h")
            : 0;
        var actionConfirmed = Boolean(wispData, "actionConfirmed");
        var outputsAttributed = Boolean(wispData, "outputsAttributed");
        var success = rejectedBasicRetained &&
                      gapLane == "basic" &&
                      gapCorrelationReset == false &&
                      !gameCounterReset &&
                      wisps.Length == 1 &&
                      wispCount == 1 &&
                      counterIncrement == 1 &&
                      candidate900h == 1 &&
                      !actionConfirmed &&
                      !outputsAttributed;
        return new(success, rejectedBasicRetained, gapLane, gapCorrelationReset,
            gameCounterReset, wisps.Length, wispCount, counterIncrement, candidate900h,
            wisps.Length > 1, actionConfirmed, outputsAttributed);
    }

    private static async IAsyncEnumerable<DiagnosticRecognitionFrame> RejectedBasicThenFullScan(
        DataCatalog catalog, DateTimeOffset rejectedAt, DateTimeOffset fullAt)
    {
        var rejected = DiagnosticBasicInventoryObservation.Unavailable(
            catalog.MapVersion, catalog.OfflineBundle!.Fingerprint,
            Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash, 9000,
            rejectedAt.AddMilliseconds(-10), rejectedAt, TimeSpan.FromMilliseconds(10),
            "Missing or unavailable typed basic observation");
        yield return DiagnosticRecognitionFrame.ForBasic(rejected, new RecognitionDiagnostics
        {
            Source = DiagnosticBasicInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version,
            ExecutableSha256 = Warcraft300Diagnostic.Hash,
            Detail = "Diagnostic CUnit allocated handle generation changed"
        });
        await Task.CompletedTask;
        yield return DiagnosticRecognitionFrame.ForCompleted(
            FullResult(catalog, 3439, 7001, fullAt, Raw(marine: 2, xi: 2), f: 14));
    }

    private static async IAsyncEnumerable<DiagnosticRecognitionFrame> FullScan(
        DataCatalog catalog, long fullRevision, long pairRevision,
        DateTimeOffset completedAt, ImmutableDictionary<string, int> rawcodes, int f)
    {
        await Task.CompletedTask;
        yield return DiagnosticRecognitionFrame.ForCompleted(
            FullResult(catalog, fullRevision, pairRevision, completedAt, rawcodes, f));
    }

    private static RecognitionResult FullResult(
        DataCatalog catalog, long fullRevision, long pairRevision,
        DateTimeOffset completedAt, ImmutableDictionary<string, int> rawcodes, int f)
    {
        var marine = catalog.AllUnits.First(unit =>
            unit.Rawcodes.Contains("900h", StringComparer.Ordinal));
        var entries = new[]
        {
            new InventoryEntry { UnitId = marine.Id, Count = rawcodes.GetValueOrDefault("900h") }
        };
        var basicDiagnostics = new RecognitionDiagnostics
        {
            Source = DiagnosticBasicInventoryObservation.SourceName,
            ProcessVersion = Warcraft300Diagnostic.Version,
            ExecutableSha256 = Warcraft300Diagnostic.Hash
        };
        var basic = DiagnosticBasicInventoryObservation.Create(
            catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            Context, pairRevision, 0, completedAt.AddMilliseconds(-10), completedAt,
            TimeSpan.FromMilliseconds(10), entries, World, Binding);
        var full = DiagnosticInventoryObservation.Create(
            catalog, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            Context, fullRevision, 0, completedAt.AddMilliseconds(-20), completedAt,
            TimeSpan.FromMilliseconds(20), entries, [], observedRound: 6,
            worldStampFingerprint: World, bindingContextId: Binding);
        return new RecognitionResult
        {
            State = RecognitionState.Ready,
            Diagnostics = new RecognitionDiagnostics
            {
                Source = DiagnosticInventoryObservation.SourceName,
                ProcessVersion = Warcraft300Diagnostic.Version,
                ExecutableSha256 = Warcraft300Diagnostic.Hash,
                ActivityRawcodes = rawcodes,
                ActivityProjectedRawcodes = rawcodes,
                ActivityCounters = ImmutableDictionary<string, int>.Empty.Add("f", f),
                ActivityCounterStatus = "partial"
            },
            DiagnosticObservation = full,
            CompletionBasicSample = new(basic, basicDiagnostics)
        };
    }

    private static ImmutableDictionary<string, int> Raw(int marine, int xi) =>
        ImmutableDictionary<string, int>.Empty
            .Add("900h", marine)
            .Add("XI0e", xi);

    private static string? Kind(JsonElement row) =>
        row.TryGetProperty("kind", out var kind) ? kind.GetString() : null;

    private static int Integer(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value)
            ? value.GetInt32()
            : 0;

    private static bool Boolean(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(name, out var value) &&
        value.GetBoolean();
}
