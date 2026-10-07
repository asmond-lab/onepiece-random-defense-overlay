using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalGameActivityDelayedWispTests
{
    private static readonly Lazy<DataCatalog> Catalog = new(() =>
    {
        var result = new DataCatalog();
        result.Load(mapVersion: "2.321");
        return result;
    });
    private static readonly Lazy<Map2321ActivityRules> Rules = new(Map2321ActivityRules.LoadBundled);

    [Fact]
    public void ActualRandomWispTraceCorrelatesAcrossUnchangedFullObservation()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(3438, DateTimeOffset.Parse("2026-09-21T23:32:57.9826206Z")),
            Diagnostics(new() { ["900h"] = 1, ["XI0e"] = 2 }, ("f", 13)), 1);
        capture.Observe(Sample(3439, DateTimeOffset.Parse("2026-09-21T23:32:58.2324350Z")),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 2 }, ("f", 14)), 1);
        capture.Observe(Sample(3440, DateTimeOffset.Parse("2026-09-21T23:32:58.4913597Z")),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 2 }, ("f", 14)), 1);
        capture.Observe(Sample(3441, DateTimeOffset.Parse("2026-09-21T23:32:58.7482911Z")),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 1 }, ("f", 14)), 1);

        var action = Assert.Single(rows, row => row.Kind == "game.wisp").Data;
        Assert.Equal("e0IX", action.GetProperty("wispId").GetString());
        Assert.Equal(1, action.GetProperty("candidateOutputs").GetProperty("900h").GetInt32());
        Assert.False(action.GetProperty("actionConfirmed").GetBoolean());
        Assert.False(action.GetProperty("outputsAttributed").GetBoolean());
        var evidence = Assert.Single(rows, row => row.Kind == "game.wisp-evidence").Data;
        Assert.True(evidence.GetProperty("correlated").GetBoolean());
        Assert.Equal("counter-first", evidence.GetProperty("arrivalOrder").GetString());
    }

    [Fact]
    public void UnchangedObservationsRetainPendingOnlyThroughExistingFreshnessBudget()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        var baseline = DateTimeOffset.Parse("2026-09-21T23:32:57Z");
        var signal = baseline.AddMilliseconds(100);
        capture.Observe(Sample(1, baseline),
            Diagnostics(new() { ["900h"] = 1, ["XI0e"] = 2 }, ("f", 13)), 1);
        capture.Observe(Sample(2, signal),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 2 }, ("f", 14)), 1);
        capture.Observe(Sample(3, signal + DiagnosticInventoryObservation.FreshnessBudget),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 2 }, ("f", 14)), 1);
        capture.Observe(Sample(4, signal + DiagnosticInventoryObservation.FreshnessBudget +
                                  TimeSpan.FromTicks(1)),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 2 }, ("f", 14)), 1);
        capture.Observe(Sample(5, signal + DiagnosticInventoryObservation.FreshnessBudget +
                                  TimeSpan.FromMilliseconds(100)),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 1 }, ("f", 14)), 1);

        Assert.Equal(2, rows.Count(row => row.Kind == "game.wisp"));
        var expired = Assert.Single(rows, row => row.Kind == "game.wisp-evidence").Data;
        Assert.False(expired.GetProperty("correlated").GetBoolean());
        Assert.Equal("freshness-budget", expired.GetProperty("reason").GetString());
    }

    [Fact]
    public void ComplementWithIncompatibleDeltaExpiresPendingAndRemainsDistinctAction()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        var baseline = DateTimeOffset.Parse("2026-09-21T23:32:57Z");
        capture.Observe(Sample(1, baseline),
            Diagnostics(new() { ["900h"] = 1, ["XI0e"] = 2 }, ("f", 13)), 1);
        capture.Observe(Sample(2, baseline.AddMilliseconds(250)),
            Diagnostics(new() { ["900h"] = 2, ["XI0e"] = 2 }, ("f", 14)), 1);
        capture.Observe(Sample(3, baseline.AddMilliseconds(500)),
            Diagnostics(new()
            {
                ["900h"] = 2, ["XI0e"] = 1, ["K00h"] = 1
            }, ("f", 14)), 1);

        Assert.Equal(2, rows.Count(row => row.Kind == "game.wisp"));
        var expired = Assert.Single(rows, row => row.Kind == "game.wisp-evidence").Data;
        Assert.False(expired.GetProperty("correlated").GetBoolean());
        Assert.Equal("incompatible-delta", expired.GetProperty("reason").GetString());
    }

    private static LocalGameActivityCapture Capture(List<(string Kind, JsonElement Data)> rows) =>
        new(Rules.Value, (kind, data, _, _) => rows.Add((kind, JsonSerializer.SerializeToElement(data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))));

    private static RecognitionDiagnostics Diagnostics(Dictionary<string, int> raw,
        params (string Name, int Value)[] counters) => new()
    {
        ActivityRawcodes = raw.ToImmutableDictionary(StringComparer.Ordinal),
        ActivityProjectedRawcodes = raw.ToImmutableDictionary(StringComparer.Ordinal),
        ActivityCounters = counters.ToImmutableDictionary(
            pair => pair.Name, pair => pair.Value, StringComparer.Ordinal),
        ActivityCounterStatus = "partial"
    };

    private static DiagnosticBasicInventoryObservation Sample(
        long revision, DateTimeOffset completedAt) =>
        DiagnosticBasicInventoryObservation.Create(Catalog.Value, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, new string('A', 64), revision, 0,
            completedAt.AddMilliseconds(-10), completedAt, TimeSpan.FromMilliseconds(10), [],
            new string('B', 64), new string('A', 64));
}
