using Xunit;

namespace OrandOverlay.Tests;

public sealed class ExpectedReadTargetTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 14, 5, 21, 7, TimeSpan.Zero);

    [Fact]
    public void ExactIdentityMatchesWithoutOpeningAProcess()
    {
        var target = new ExpectedReadTarget(50744, Started);
        Assert.True(target.Matches(50744, Started.UtcTicks));
        target.EnsureMatches(50744, Started.UtcTicks);
        Assert.Equal(Started.UtcTicks, target.StartedAtUtcTicks);
    }

    [Theory]
    [InlineData(50745, 0)]
    [InlineData(50744, 1)]
    [InlineData(50745, 1)]
    public void PidReuseAndNewerProcessAreRejectedPurely(int pid, long addedTicks)
    {
        var target = new ExpectedReadTarget(50744, Started);
        Assert.False(target.Matches(pid, Started.UtcTicks + addedTicks));
        Assert.Throws<ReadTargetMismatchException>(() => target.EnsureMatches(pid, Started.UtcTicks + addedTicks));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void InvalidPidRejected(int pid) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpectedReadTarget(pid, Started));

    [Fact]
    public void MissingStartRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExpectedReadTarget(50744, default));

    [Fact]
    public void TimestampComparisonUsesUtcNotDisplayedOffset()
    {
        var target = new ExpectedReadTarget(50744, Started.ToOffset(TimeSpan.FromHours(9)));
        Assert.True(target.Matches(50744, Started.UtcTicks));
    }

    [Fact]
    public void ProductionGuardImmediatelyFollowsNullHandlingBeforeModuleHashOrMemoryOpen()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "WarcraftMemoryRecognitionService.cs"));
        var selection = source.IndexOf("using var process = FindNewestProcess", StringComparison.Ordinal);
        var nullCheck = source.IndexOf("if (process is null)", selection, StringComparison.Ordinal);
        var guard = source.IndexOf("_expectedReadTarget?.EnsureMatches(process.Id, process.StartTime.ToUniversalTime().Ticks);", nullCheck, StringComparison.Ordinal);
        var module = source.IndexOf("var mainModule = process.MainModule;", selection, StringComparison.Ordinal);
        var hash = source.IndexOf("ExecutableHashCache.Sha256", selection, StringComparison.Ordinal);
        var memory = source.IndexOf("ReadOnlyProcessMemory.Open(process.Id)", selection, StringComparison.Ordinal);
        Assert.True(selection >= 0 && nullCheck > selection && guard > nullCheck);
        Assert.True(module > guard && hash > guard && memory > guard);
        var afterNullBlock = source[(source.IndexOf('}', nullCheck) + 1)..guard];
        Assert.All(afterNullBlock.Split('\n'), line => Assert.True(string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//")));
        Assert.Contains(": this(catalog, userRoot, null)", source);
        Assert.Contains("public WarcraftMemoryRecognitionService(DataCatalog catalog)", source);
        Assert.False(typeof(InvalidOperationException).IsAssignableFrom(typeof(ReadTargetMismatchException)));
    }

    [Fact]
    public void CliRemainsBoundedAndUsesProductionReader()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "Tools", "RandyPickLiveValidation", "Program.cs"));
        Assert.Contains("samples is < 1 or > 3", source);
        Assert.Contains("TimeSpan.FromSeconds(45)", source);
        Assert.Contains("new WarcraftMemoryRecognitionService(catalog, userRoot, options.Target)", source);
        Assert.Contains("catalog.Load(mapVersion: \"2.320\")", source);
        Assert.Contains("FileMode.CreateNew", source);
        Assert.Contains("TelemetryEnabled = false", source);
        Assert.DoesNotContain("VirtualProtect", source);
        Assert.DoesNotContain("WriteProcessMemory", source);
        Assert.DoesNotContain("MemoryDiagnostics", source);
    }

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "WarcraftMemoryRecognitionService.cs"))) return dir.FullName;
        throw new DirectoryNotFoundException("Source root required for source-order regression tests.");
    }
}
