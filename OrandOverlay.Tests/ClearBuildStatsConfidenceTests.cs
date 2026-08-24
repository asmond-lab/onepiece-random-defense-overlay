using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ClearBuildStatsConfidenceTests
{
    [Fact]
    public void ThinProfileRequiresWilsonConfidenceBeforeDeclaringCore()
    {
        var samples = Enumerable.Range(0, 12)
            .Select(index => new ClearSample(
                $"sample-{index}",
                new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
                "신",
                3,
                [
                    new ClearSampleUnit("GOAL", 1, "초월 [물딜]"),
                    new ClearSampleUnit("STRONG", 1, "전설 [물딜]"),
                    .. index < 3
                        ? new[] { new ClearSampleUnit("MARGINAL", 1, "전설 [물딜]") }
                        : [new ClearSampleUnit(
                            $"FILLER-{index}", 1, "전설 [물딜]")]
                ]))
            .ToList();
        var stats = ClearBuildStats.FromSamples(samples);

        Assert.True(stats.IsCore(["GOAL"], ["STRONG"]));
        Assert.False(stats.IsCore(["GOAL"], ["MARGINAL"]));
    }
}
