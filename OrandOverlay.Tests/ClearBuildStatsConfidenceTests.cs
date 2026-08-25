using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ClearBuildStatsConfidenceTests
{
    [Fact]
    public void MultiTopScopeCountsDistinctFieldFormsBeforeCanonicalDeduplication()
    {
        var samples = Enumerable.Range(0, ClearBuildStats.MinimumGoalSamples)
            .Select(index => new ClearSample(
                $"multi-form-{index}",
                new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
                "신",
                3,
                [
                    new ClearSampleUnit("Q40h", 1, "불멸 [마딜]"),
                    new ClearSampleUnit("H90H", 1, "초월 [마딜]"),
                    new ClearSampleUnit("G90H", 1, "초월 [마딜]")
                ]))
            .ToList();

        var profile = ClearBuildStats.FromSamples(samples)
            .GoalProfile(["Q40h"], TopScope.MultiTop);

        Assert.NotNull(profile);
        Assert.Equal(TopScope.MultiTop, profile.Scope);
        Assert.Equal(ClearBuildStats.MinimumGoalSamples, profile.SampleCount);
    }

    [Fact]
    public void BundledBigMomMultiTopProfileIncludesAllEligibleSamples()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data",
            "tmo-clear-samples.json");
        var samples = ClearSampleDocument.Parse(File.ReadAllText(path));
        var expected = samples
            .Select(sample => sample with
            {
                Units = sample.Units
                    .Select(unit => unit with
                    {
                        Code = RawcodeAliases.Canonical(unit.Code)
                    })
                    .ToList()
            })
            .Where(sample => sample.Difficulty is "신" or "악몽")
            .GroupBy(sample => sample.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .Count(sample =>
                sample.Units.Any(unit => unit.Code == "Q40h") &&
                sample.Units
                    .GroupBy(unit => unit.Code, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .Count(unit => IsTopGrade(unit.Grade)) >= 2);

        var profile = ClearBuildStats.FromSamples(samples)
            .GoalProfile(["Q40h"], TopScope.MultiTop);

        Assert.NotNull(profile);
        Assert.Equal(expected, profile.SampleCount);
        Assert.True(profile.SampleCount >= 300);
    }


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

    private static bool IsTopGrade(string grade) =>
        new[] { "초월", "불멸", "영원", "제한" }
            .Any(prefix => grade.StartsWith(prefix, StringComparison.Ordinal));
}
