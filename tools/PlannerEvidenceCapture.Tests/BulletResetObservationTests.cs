using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class BulletResetObservationTests
{
    [Theory]
    [InlineData("queen-event-reset", "HA0h", "300h")]
    [InlineData("stop-event-reset", "300h", null)]
    public void ResetRequestReachesReadyBoundaryBeforeLowerRoundCorrelation(string name, string first, string? second)
    {
        // Headless request/policy/predicate replay, NOT execution of MainWindow or WPF.
        // 60 -> 2 and rawcodes are the actual Queen/Stop capture inputs.
        var catalog = new DataCatalog();
        catalog.Load();
        var entries = new[] { first, second }.Where(code => code is not null).Select(code =>
            new InventoryEntry { UnitId = catalog.AllUnits.First(u => u.Rawcodes.Contains(code!)).Id, Count = 1 }).ToList();
        var request = new RecognitionResult { State = RecognitionState.Ready, Entries = entries,
            ConfirmsSessionBoundary = BulletGuideRowProjection.ConfirmsSessionBoundary(name) };
        var resets = RecognitionPolicy.ShouldResetBeforeReadyInventory(request);
        // Replay the source-checked ScanCoreAsync round transition; identity numbers are symbolic,
        // not claimed to have been logged by the failed WPF run.
        const long previousGeneration = 7, previousRevision = 91;
        var inventory = entries.ToImmutableDictionary(e => e.UnitId, e => e.Count);
        var marker = GoroseiMarkerSnapshot.Unknown;
        var frame = new CoachFrame { Revision = 1, CompletedStoryStage = 0, IsCurrent = true,
            Round = Math.Max(resets ? 0 : 60, 2),
            MatchGeneration = previousGeneration + (resets ? 1 : 0), RecognitionRevision = previousRevision + 1,
            Inventory = inventory, Gorosei = new(previousGeneration + (resets ? 1 : 0), previousRevision + 1,
                GoroseiMode.None, true) { Marker = marker } };
        Assert.True(BulletGuideRowProjection.MatchesObservation(frame, 2, frame.MatchGeneration,
            previousRevision + 1, inventory, marker), $"{name}: requested round 2, produced round {frame.Round}; Ready boundary={resets}");
        Assert.True(resets);
        Assert.False(BulletGuideRowProjection.MatchesObservation(frame with { RecognitionRevision = previousRevision },
            2, frame.MatchGeneration, previousRevision + 1, inventory, marker));
        Assert.False(BulletGuideRowProjection.MatchesObservation(frame with { MatchGeneration = previousGeneration },
            2, previousGeneration + 1, previousRevision + 1, inventory, marker));
    }

    [Theory]
    [InlineData("queen-event-owned")]
    [InlineData("stop-event-old-combat")]
    [InlineData("stop-common-transition-resumed")]
    public void NonBoundaryAndResumeDoNotResetTheMatch(string name) =>
        Assert.False(BulletGuideRowProjection.ConfirmsSessionBoundary(name));

    [Fact]
    public void ReplayRoundTransitionRemainsBoundToProductionSource()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OrandOverlay.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var main = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml.cs"));
        Assert.Contains("if (RecognitionPolicy.ShouldResetBeforeReadyInventory(result))\n            {\n                ResetMatchSession();\n                _lastRound = 0;\n            }", main.Replace("\r\n", "\n"));
        Assert.Contains("_lastRound = mapState.MaxRound > 0 ? Math.Max(_lastRound, mapState.MaxRound) : 0;", main);
        var coach = File.ReadAllText(Path.Combine(root.FullName, "MainWindow.Coach.cs"));
        Assert.Contains("Round = _lastRound", coach);
        var capture = File.ReadAllText(Path.Combine(root.FullName, "tools/PlannerEvidenceCapture/BulletGuideCapture.cs"));
        Assert.Contains("ConfirmsSessionBoundary = BulletGuideRowProjection.ConfirmsSessionBoundary(name)", capture);
    }
}
