using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalGameActivityCaptureTests
{
    private static readonly Lazy<DataCatalog> Catalog = new(() =>
    {
        var result = new DataCatalog();
        result.Load(mapVersion: "2.321");
        return result;
    });
    private static readonly Lazy<Map2321ActivityRules> Rules = new(Map2321ActivityRules.LoadBundled);

    [Fact]
    public void SelectionCounterAndCompatibleOutputAreRecordedWithExplicitEvidence()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), Diagnostics(new() { ["810e"] = 1, ["300h"] = 1 }), 1);
        capture.Observe(Sample(2), Diagnostics(new() { ["300h"] = 2 }, "NU"), 1);

        var action = Assert.Single(rows, row => row.Kind == "game.wisp").Data;
        Assert.Equal("e018", action.GetProperty("wispId").GetString());
        Assert.Equal(1, action.GetProperty("count").GetInt32());
        Assert.Equal(1, action.GetProperty("candidateOutputs").GetProperty("300h").GetInt32());
        Assert.False(action.GetProperty("actionConfirmed").GetBoolean());
    }

    [Fact]
    public void CounterFirstSelectionEvidenceProducesOneActionAndOneCorrelationUpdate()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), PartialDiagnostics(new() { ["810e"] = 1 }, ("NU", 1)), 1);
        capture.Observe(Sample(2), PartialDiagnostics(
            new() { ["810e"] = 1, ["300h"] = 1 }, ("NU", 2)), 1);
        capture.Observe(Sample(3), PartialDiagnostics(new() { ["300h"] = 1 }, ("NU", 2)), 1);

        var action = Assert.Single(rows, row => row.Kind == "game.wisp").Data;
        Assert.Equal(1, action.GetProperty("count").GetInt32());
        var correlation = Assert.Single(rows, row => row.Kind == "game.wisp-evidence").Data;
        Assert.True(correlation.GetProperty("correlated").GetBoolean());
        Assert.Equal("counter-first", correlation.GetProperty("arrivalOrder").GetString());
        Assert.Equal(1, correlation.GetProperty("matchedCount").GetInt32());
    }

    [Fact]
    public void RawFirstSelectionEvidenceProducesOneActionAndOneCorrelationUpdate()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), PartialDiagnostics(new() { ["810e"] = 1 }, ("NU", 1)), 1);
        capture.Observe(Sample(2), PartialDiagnostics(new() { ["300h"] = 1 }, ("NU", 1)), 1);
        capture.Observe(Sample(3), PartialDiagnostics(new() { ["300h"] = 1 }, ("NU", 2)), 1);

        var action = Assert.Single(rows, row => row.Kind == "game.wisp").Data;
        Assert.Equal(1, action.GetProperty("count").GetInt32());
        var correlation = Assert.Single(rows, row => row.Kind == "game.wisp-evidence").Data;
        Assert.True(correlation.GetProperty("correlated").GetBoolean());
        Assert.Equal("raw-first", correlation.GetProperty("arrivalOrder").GetString());
        Assert.Equal(1, correlation.GetProperty("matchedCount").GetInt32());
    }

    [Fact]
    public void UnrelatedObservationExpiresWispCorrelationWithoutConsumingLaterAction()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), PartialDiagnostics(new() { ["810e"] = 2 }, ("NU", 1)), 1);
        capture.Observe(Sample(2), PartialDiagnostics(
            new() { ["810e"] = 2, ["300h"] = 1 }, ("NU", 2)), 1);
        capture.Observe(Sample(3), PartialDiagnostics(
            new() { ["810e"] = 2, ["300h"] = 1, ["K00h"] = 1 }, ("NU", 2)), 1);
        capture.Observe(Sample(4), PartialDiagnostics(
            new() { ["810e"] = 1, ["300h"] = 1, ["K00h"] = 1 }, ("NU", 2)), 1);

        Assert.Equal(2, rows.Count(row => row.Kind == "game.wisp"));
        var expired = Assert.Single(rows, row => row.Kind == "game.wisp-evidence").Data;
        Assert.False(expired.GetProperty("correlated").GetBoolean());
        Assert.Equal("unrelated-observation", expired.GetProperty("reason").GetString());
    }

    [Fact]
    public void ExactCurrentMapRecipeRecordsConsumedIngredientsAndResult()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), Diagnostics(new() { ["300h"] = 1, ["900h"] = 1 }), 1);
        capture.Observe(Sample(2), Diagnostics(new() { ["K00h"] = 1 }), 1);

        var craft = Assert.Single(rows, row => row.Kind == "game.craft").Data;
        Assert.Equal("A00D", craft.GetProperty("recipeId").GetString());
        Assert.Equal("K00h", craft.GetProperty("outputRawcode").GetString());
        Assert.Equal(1, craft.GetProperty("consumed").GetProperty("300h").GetInt32());
        Assert.Equal(1, craft.GetProperty("consumed").GetProperty("900h").GetInt32());
        Assert.False(craft.GetProperty("actionConfirmed").GetBoolean());
    }

    [Fact]
    public void GambleCountersRecordResolvedOutcomeButAmbiguousUnitGainsStayUnattributed()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), Diagnostics(new()), 1);
        capture.Observe(Sample(2), Diagnostics(new() { ["300h"] = 1, ["400h"] = 1 }, "Ie", "tU"), 1);

        var gamble = Assert.Single(rows, row => row.Kind == "game.gamble").Data;
        Assert.Equal("low", gamble.GetProperty("gambleId").GetString());
        Assert.Equal(1, gamble.GetProperty("attempts").GetInt32());
        Assert.Equal(1, gamble.GetProperty("successes").GetInt32());
        Assert.Equal(0, gamble.GetProperty("failures").GetInt32());
        Assert.False(gamble.GetProperty("outputsAttributed").GetBoolean());
    }

    [Fact]
    public void KnownGambleAttemptSurvivesMissingOutcomeCounters()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), PartialDiagnostics(new(), ("Ie", 1), ("tU", 1)), 1);
        capture.Observe(Sample(2), PartialDiagnostics(
            new() { ["300h"] = 1 }, ("Ie", 2), ("tU", 2)), 1);

        var gamble = Assert.Single(rows, row => row.Kind == "game.gamble").Data;
        Assert.Equal("low", gamble.GetProperty("gambleId").GetString());
        Assert.Equal(1, gamble.GetProperty("attempts").GetInt32());
        Assert.False(gamble.GetProperty("outcomeComplete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, gamble.GetProperty("successes").ValueKind);
        Assert.Equal(JsonValueKind.Null, gamble.GetProperty("failures").ValueKind);
        Assert.Equal(1, gamble.GetProperty("observedOutcomeCounterDeltas").GetProperty("tU").GetInt32());
        var missing = gamble.GetProperty("missingCounterNames").EnumerateArray()
            .Select(item => item.GetString()).ToArray();
        Assert.Contains("xe", missing);
        Assert.Contains("gU", missing);
    }

    [Fact]
    public void ResetAndCounterRollbackNeverInventAnActionAcrossMissingCoverage()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), Diagnostics(new()), 1);
        capture.Reset();
        capture.Observe(Sample(2), Diagnostics(new() { ["300h"] = 1 }, "NU"), 1);
        capture.Observe(Sample(3), Diagnostics(new() { ["300h"] = 1 }), 1);

        Assert.Contains(rows, row => row.Kind == "game.counter-reset");
        Assert.DoesNotContain(rows, row => row.Kind is "game.wisp" or "game.craft" or "game.gamble");
    }

    [Fact]
    public void MissingCounterKeyDoesNotBecomeZeroOrBridgeUnobservedCoverage()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);

        capture.Observe(Sample(1), PartialDiagnostics(new(), ("NU", 4)), 1);
        capture.Observe(Sample(2), PartialDiagnostics(new() { ["300h"] = 1 }), 1);
        capture.Observe(Sample(3), PartialDiagnostics(new() { ["300h"] = 1 }, ("NU", 5)), 1);

        Assert.DoesNotContain(rows, row => row.Kind is "game.wisp" or "game.gamble");
        Assert.DoesNotContain(rows, row => row.Kind == "game.counter" &&
            row.Data.GetProperty("increments").TryGetProperty("NU", out _));
    }

    [Fact]
    public void NewlyObservedCumulativeCounterIsRecordedWithoutInventingDelta()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        capture.Observe(Sample(1), PartialDiagnostics(new()), 1);
        capture.Observe(Sample(2), PartialDiagnostics(new(), ("NU", 5)), 1);

        var observed = Assert.Single(rows, row => row.Kind == "game.counter-observed").Data;
        Assert.Equal(5, observed.GetProperty("values").GetProperty("NU").GetInt32());
        Assert.False(observed.GetProperty("deltaKnown").GetBoolean());
        Assert.DoesNotContain(rows, row => row.Kind is "game.wisp" or "game.counter");
    }

    [Fact]
    public void MixedInventoryDeltaIsNotAnExactCraftAndCandidateGainsStayUnattributed()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);

        capture.Observe(Sample(1), Diagnostics(new()
        {
            ["300h"] = 1, ["900h"] = 1
        }), 1);
        capture.Observe(Sample(2), Diagnostics(new()
        {
            ["K00h"] = 1, ["400h"] = 1
        }, "Ie", "tU"), 1);

        Assert.DoesNotContain(rows, row => row.Kind == "game.craft");
        Assert.All(rows.Where(row => row.Kind is "game.wisp" or "game.gamble"),
            row => Assert.False(row.Data.GetProperty("outputsAttributed").GetBoolean()));
    }

    [Fact]
    public void GenerationChangeFencesInventoryAndCounterDeltas()
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);

        capture.Observe(Sample(1), Diagnostics(new() { ["810e"] = 1 }), 1);
        capture.Observe(Sample(2), Diagnostics(new() { ["300h"] = 1 }, "NU"), 2);

        var reset = Assert.Single(rows, row => row.Kind == "game.counter-reset").Data;
        Assert.Equal("context-changed", reset.GetProperty("reason").GetString());
        Assert.DoesNotContain(rows, row => row.Kind is "game.wisp" or "game.craft" or "game.gamble");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExactRecipeCorrelatesOutputAndConsumptionAcrossEitherAdjacentOrder(bool outputFirst)
    {
        var rows = new List<(string Kind, JsonElement Data)>();
        var capture = Capture(rows);
        var ingredients = new Dictionary<string, int> { ["300h"] = 1, ["900h"] = 1 };
        capture.Observe(Sample(1), Diagnostics(ingredients), 1);
        capture.Observe(Sample(2), Diagnostics(outputFirst
            ? new() { ["300h"] = 1, ["900h"] = 1, ["K00h"] = 1 }
            : new()), 1);
        capture.Observe(Sample(3), Diagnostics(new() { ["K00h"] = 1 }), 1);

        var craft = Assert.Single(rows, row => row.Kind == "game.craft").Data;
        Assert.Equal("A00D", craft.GetProperty("recipeId").GetString());
        Assert.Equal("adjacent-observation-correlation",
            craft.GetProperty("correlation").GetString());
        Assert.Equal(1, craft.GetProperty("consumed").GetProperty("300h").GetInt32());
        Assert.Equal(1, craft.GetProperty("consumed").GetProperty("900h").GetInt32());
    }

    [Fact]
    public void AlteredRuleBundleCannotProvideActionAttribution()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", Map2321ActivityRules.FileName));
        bytes[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => Map2321ActivityRules.Load(bytes));
    }

    private static LocalGameActivityCapture Capture(List<(string Kind, JsonElement Data)> rows) =>
        new(Rules.Value, (kind, data, _, _) => rows.Add((kind, JsonSerializer.SerializeToElement(data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))));

    private static RecognitionDiagnostics Diagnostics(Dictionary<string, int> raw, params string[] incremented) => new()
    {
        ActivityRawcodes = raw.ToImmutableDictionary(StringComparer.Ordinal),
        ActivityProjectedRawcodes = raw.ToImmutableDictionary(StringComparer.Ordinal),
        ActivityCounters = Rules.Value.IntegerArrays.ToImmutableDictionary(name => name,
            name => incremented.Contains(name, StringComparer.Ordinal) ? 1 : 0, StringComparer.Ordinal),
        ActivityCounterStatus = "ready"
    };

    private static RecognitionDiagnostics PartialDiagnostics(Dictionary<string, int> raw,
        params (string Name, int Value)[] counters) => new()
    {
        ActivityRawcodes = raw.ToImmutableDictionary(StringComparer.Ordinal),
        ActivityProjectedRawcodes = raw.ToImmutableDictionary(StringComparer.Ordinal),
        ActivityCounters = counters.ToImmutableDictionary(
            pair => pair.Name, pair => pair.Value, StringComparer.Ordinal),
        ActivityCounterStatus = "partial"
    };

    private static DiagnosticBasicInventoryObservation Sample(long revision)
    {
        var completedAt = DateTimeOffset.UtcNow;
        return DiagnosticBasicInventoryObservation.Create(Catalog.Value, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, new string('A', 64), revision, 0,
            completedAt.AddMilliseconds(-10), completedAt, TimeSpan.FromMilliseconds(10), [],
            new string('B', 64), new string('A', 64));
    }
}
