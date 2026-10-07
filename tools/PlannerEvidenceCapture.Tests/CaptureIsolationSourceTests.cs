using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class CaptureIsolationSourceTests
{
    internal static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrandOverlay.csproj"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tools", "PlannerEvidenceCapture");
    }

    [Fact]
    public void AllCaptureWritesRouteThroughScopeAndPreflightPrecedesApp()
    {
        var sources = Directory.GetFiles(SourceRoot(), "*.cs")
            .Where(path => !path.EndsWith("CaptureOutputScope.cs", StringComparison.Ordinal)).Select(File.ReadAllText).ToArray();
        foreach (var source in sources)
        foreach (var unsafeCall in new[] { "File.WriteAllText(", "File.Create(", "File.Copy(", "Directory.CreateDirectory(", "Directory.Delete(", "new CoachJournal(" })
            Assert.DoesNotContain(unsafeCall, source);
        var program = File.ReadAllText(Path.Combine(SourceRoot(), "Program.cs"));
        Assert.True(program.IndexOf("CapturePreflight.ValidateArguments(args)", StringComparison.Ordinal) < program.IndexOf("new App", StringComparison.Ordinal));
        Assert.Contains("using var outputScope = CaptureOutputScope.Create(Path.GetTempPath(), output)", program);
        Assert.Contains("CaptureInputContract.ValidateBundledInputs", program);
    }

    [Fact]
    public void ControlledConstructorsUseExplicitFixtureAndWindowSettings()
    {
        var sources = Directory.GetFiles(SourceRoot(), "*.cs").Select(File.ReadAllText).ToArray();
        Assert.All(sources, source => Assert.DoesNotContain("startRuntime:", source));
        Assert.All(sources, source => Assert.DoesNotContain("SkipRuntimeStartup", source));
        foreach (var name in new[] { "CoachAppCapture.cs", "FourModeCapture.cs" })
            Assert.Contains("settings = FixtureSettings(main);", File.ReadAllText(Path.Combine(SourceRoot(), name)));
        Assert.Contains("Surface = \"ScanControlledAsync", File.ReadAllText(Path.Combine(SourceRoot(), "ReadyBoundaryCapture.cs")));
    }

    [Fact]
    public void JournalCapturesUseFixtureJournalSeamWithoutPathReinjection()
    {
        foreach (var name in new[] { "CoachAppCapture.cs", "CoachFinishedRewardCapture.cs", "FourModeCapture.cs" })
        {
            var source = File.ReadAllText(Path.Combine(SourceRoot(), name));
            Assert.Contains("FixtureJournalContext(", source);
            Assert.Contains("FixtureJournal(main)", source);
            Assert.DoesNotContain("SetValue(main, journal)", source);
            Assert.DoesNotContain("RejectPathBasedFixtureJournal", source);
            Assert.DoesNotContain("CoachReview.Read(journal.LatestPath", source);
            Assert.Contains("CoachReview.Read(journal)", source);
        }
        var helper = File.ReadAllText(Path.Combine(SourceRoot(), "FixtureSettings.cs"));
        Assert.Contains("FixtureWithMemoryJournal", helper);
        Assert.DoesNotContain("RejectPathBasedFixtureJournal", helper);
        Assert.DoesNotContain("new CoachJournal(", helper);
    }
}
