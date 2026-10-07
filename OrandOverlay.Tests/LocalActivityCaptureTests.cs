using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalActivityCaptureTests
{
    private static readonly Lazy<DataCatalog> Catalog = new(() =>
    {
        var value = new DataCatalog();
        value.Load(mapVersion: "2.321");
        return value;
    });

    [Fact]
    public void RecordsEveryReadWithOriginalSampleTimesAndRejectionReason()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        var sample = Basic(1, 1);
        capture.Observe("basic", sample, new() { Detail = "native-detail" }, true, "none", 4);
        capture.Observe("basic", Basic(2, 1), new() { Detail = "allocation-rejected" },
            false, "binding", 4);

        var reads = rows.Where(row => row.Kind == "memory.read").ToArray();
        Assert.Equal(2, reads.Length);
        Assert.Equal(sample.StartedAt, reads[0].Data.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(sample.CompletedAt, reads[0].Data.GetProperty("completedAt").GetDateTimeOffset());
        Assert.False(reads[1].Data.GetProperty("accepted").GetBoolean());
        Assert.Equal("allocation-rejected", reads[1].Data.GetProperty("detail").GetString());
    }

    [Fact]
    public void EmitsSignedInventoryChangeWithoutClaimingAConfirmedGameAction()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe("basic", Basic(1, 1), new(), true, "none", 4);
        capture.Observe("basic", Basic(2, 2), new(), true, "none", 4);

        var delta = Assert.Single(rows, row => row.Kind == "inventory.delta").Data;
        Assert.Equal(1, delta.GetProperty("added").GetProperty("rawcode:I10h").GetInt32());
        Assert.Equal("observed-current-view", delta.GetProperty("evidence").GetString());
        Assert.False(delta.GetProperty("actionConfirmed").GetBoolean());
    }

    [Fact]
    public void NeverTreatsBasicFullLaneDifferencesOrCoverageGapsAsUnitChanges()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe("basic", Basic(1, 1), new(), true, "none", 4);
        capture.Observe("full", Basic(1, 2), new(), true, "none", 4);
        capture.Gap("read-rejected", 4);
        capture.Observe("basic", Basic(2, 3), new(), true, "none", 4);
        capture.Observe("basic", Basic(3, 4, new string('D', 64)), new(), true, "none", 4);

        var gap = Assert.Single(rows, row => row.Kind == "observation.gap").Data;
        Assert.True(gap.GetProperty("correlationReset").GetBoolean());
        Assert.False(gap.TryGetProperty("lane", out _));
        Assert.DoesNotContain(rows, row => row.Kind == "inventory.delta");
    }

    [Fact]
    public void RejectedBasicFencesOnlyBasicLaneAndPreservesFullHistory()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe("full", Basic(1, 1), new(), true, "none", 4);
        capture.Observe("basic", Basic(1, 1), new(), true, "none", 4);
        capture.Observe("basic", Basic(2, 1), new() { Detail = "allocated generation changed" },
            false, "binding", 4);
        capture.Observe("full", Basic(2, 2), new(), true, "none", 4);

        var gap = Assert.Single(rows, row => row.Kind == "observation.gap").Data;
        Assert.Equal("basic", gap.GetProperty("lane").GetString());
        Assert.True(gap.GetProperty("laneReset").GetBoolean());
        Assert.False(gap.GetProperty("correlationReset").GetBoolean());
        var delta = Assert.Single(rows, row => row.Kind == "inventory.delta").Data;
        Assert.Equal("full", delta.GetProperty("lane").GetString());
        Assert.Equal(1, delta.GetProperty("added").GetProperty("rawcode:I10h").GetInt32());
    }

    [Fact]
    public void RejectedFullFencesItsOwnHistoryBeforeTheNextFullObservation()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe("full", Basic(1, 1), new(), true, "none", 4);
        capture.Observe("full", Basic(2, 1), new(), false, "full-rejected", 4);
        capture.Observe("full", Basic(3, 2), new(), true, "none", 4);

        var gap = Assert.Single(rows, row => row.Kind == "observation.gap").Data;
        Assert.Equal("full", gap.GetProperty("lane").GetString());
        Assert.True(gap.GetProperty("laneReset").GetBoolean());
        Assert.False(gap.GetProperty("correlationReset").GetBoolean());
        Assert.DoesNotContain(rows, row => row.Kind == "inventory.delta");
    }

    [Fact]
    public void UnknownWorldRawcodeFlickerDoesNotEmitRawcodeDelta()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        var owned = ImmutableDictionary<string, int>.Empty.Add("I10h", 1);
        var withUnknown = new RecognitionDiagnostics
        {
            ActivityRawcodes = owned.Add("5C0h", 1),
            UnknownRawcodes = ["5C0h"]
        };
        var withoutUnknown = new RecognitionDiagnostics { ActivityRawcodes = owned };
        capture.Observe("full", Basic(1, 1), withoutUnknown, true, "none", 4);
        capture.Observe("full", Basic(2, 1), withUnknown, true, "none", 4);
        capture.Observe("full", Basic(3, 1), withoutUnknown, true, "none", 4);
        Assert.DoesNotContain(rows, row => row.Kind == "rawcode.delta");
        Assert.DoesNotContain(rows, row => row.Kind == "inventory.delta");
    }

    [Fact]
    public void MemoryReadPreservesUnavailableCountersAndNativeReadCost()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe("full", Basic(1, 1), new()
        {
            ActivityUnavailableCounterNames = ["f", "NU"],
            ActivityCounterReadCalls = 3,
            ActivityCounterReadBytes = 192,
            ActivityCounterReadDurationMs = 0.75
        }, true, "none", 4);

        var read = Assert.Single(rows, row => row.Kind == "memory.read").Data;
        Assert.Equal(["f", "NU"], read.GetProperty("activityUnavailableCounterNames")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(3, read.GetProperty("activityCounterReadCalls").GetInt32());
        Assert.Equal(192, read.GetProperty("activityCounterReadBytes").GetInt32());
        Assert.Equal(0.75, read.GetProperty("activityCounterReadDurationMs").GetDouble());
    }

    private static LocalActivityCapture Capture(List<(string Kind, JsonElement Data)> rows) =>
        new(Catalog.Value, (kind, data, _, _) =>
            rows.Add((kind, JsonSerializer.SerializeToElement(data,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))));

    private static DiagnosticBasicInventoryObservation Basic(long revision, int count, string? binding = null)
    {
        var now = DateTimeOffset.UtcNow;
        binding ??= new string('A', 64);
        return DiagnosticBasicInventoryObservation.Create(Catalog.Value, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, binding, revision, 0, now.AddMilliseconds(-10), now,
            TimeSpan.FromMilliseconds(10), [new() { UnitId = "rawcode:I10h", Count = count }],
            new string('B', 64), binding);
    }
}
