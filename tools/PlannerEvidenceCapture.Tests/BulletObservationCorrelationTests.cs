using System.Collections.Immutable;
using OrandOverlay;
using Xunit;
namespace PlannerEvidenceCapture.Tests;
public sealed class BulletObservationCorrelationTests
{
    [Theory]
    [InlineData("generation")] [InlineData("revision")] [InlineData("inventory")]
    [InlineData("marker")] [InlineData("round")]
    public void SameRoundOldRenderCannotSatisfyRequestedObservation(string fault)
    {
        var marker = new GoroseiMarkerSnapshot(GoroseiMarkerStatus.SelectedIdentity, GoroseiMode.Saturn, "requested", []);
        var inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 20);
        var frame = new CoachFrame { Revision = 42, CompletedStoryStage = 13, IsCurrent = true, Round = 60, MatchGeneration = 0, RecognitionRevision = 42,
            Inventory = inventory, Gorosei = new(0, 42, GoroseiMode.Saturn, true) { Marker = marker } };
        Assert.True(BulletGuideRowProjection.MatchesObservation(frame, 60, 0, 42, inventory, marker));
        var stale = fault switch {
            "generation" => frame with { MatchGeneration = 1 },
            "revision" => frame with { RecognitionRevision = 41 },
            "inventory" => frame with { Inventory = inventory.SetItem("luffy_common", 30) },
            "marker" => frame with { Gorosei = frame.Gorosei with { Marker = GoroseiMarkerSnapshot.Unknown } },
            _ => frame with { Round = 49 } };
        Assert.False(BulletGuideRowProjection.MatchesObservation(stale, 60, 0, 42, inventory, marker));
    }
}
