using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LocalActivityRecognitionTests
{
    [Fact]
    public void BasicSampleRetainsValidatedRawCountsIncludingUnmappedHelpersForLocalRecording()
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.321");
        Assert.True(RawcodeCodec.TryParse("I10h", out var card));
        Assert.True(RawcodeCodec.TryParse("810e", out var helper));
        var counts = new Dictionary<uint, int> { [card] = 2, [helper] = 3 };
        var snapshot = new Warcraft300Diagnostic.Inventory(new(0x10000, 0, 0x20000), 5, 5, 0, counts);
        var now = DateTimeOffset.UtcNow;
        var sample = new WarcraftMemoryRecognitionService(catalog, Path.GetTempPath())
            .CreateBasicInventorySample(snapshot,
                new Warcraft300WorldLocator.Context(1, 2, 3, 4, 5, 6, 7, 0x30000, 0x40000),
                new MemoryProfile { MinimumCatalogMatchRatio = 0, RequireNonEmptyInventory = false },
                new RecognitionDiagnostics
                {
                    ProcessVersion = Warcraft300Diagnostic.Version,
                    ExecutableSha256 = Warcraft300Diagnostic.Hash
                }, "activity-fixture", 1, now, now, TimeSpan.Zero);

        Assert.NotNull(sample.Diagnostics.ActivityRawcodes);
        Assert.Equal(2, sample.Diagnostics.ActivityRawcodes["I10h"]);
        Assert.Equal(3, sample.Diagnostics.ActivityRawcodes["810e"]);
        counts[helper] = 0;
        Assert.Equal(3, sample.Diagnostics.ActivityRawcodes["810e"]);
        Assert.DoesNotContain("ActivityRawcodes",
            System.Text.Json.JsonSerializer.Serialize(sample.Diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityCounterDiagnosticsRemainLocalAndKeepUnavailableNamesDistinctFromZero()
    {
        var diagnostics = new RecognitionDiagnostics
        {
            ActivityCounters = new Dictionary<string, int> { ["NU"] = 7 }
                .ToImmutableDictionary(StringComparer.Ordinal),
            ActivityUnavailableCounterNames = ["f"],
            ActivityCounterStatus = "partial",
            ActivityCounterReadCalls = 41,
            ActivityCounterReadBytes = 644,
            ActivityCounterReadDurationMs = 1.25
        };

        Assert.Equal(7, diagnostics.ActivityCounters["NU"]);
        Assert.False(diagnostics.ActivityCounters.ContainsKey("f"));
        Assert.Collection(diagnostics.ActivityUnavailableCounterNames, name => Assert.Equal("f", name));
        Assert.Equal(41, diagnostics.ActivityCounterReadCalls);
        Assert.Equal(644, diagnostics.ActivityCounterReadBytes);
        Assert.Equal(1.25, diagnostics.ActivityCounterReadDurationMs);
        var json = System.Text.Json.JsonSerializer.Serialize(diagnostics);
        Assert.DoesNotContain("ActivityCounters", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityUnavailableCounterNames", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityCounterRead", json, StringComparison.Ordinal);
    }
}
