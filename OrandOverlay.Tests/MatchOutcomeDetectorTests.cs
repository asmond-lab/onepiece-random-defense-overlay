using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class MatchOutcomeDetectorTests
{
    [Fact]
    public void Observe_PreservesMultiplayerWipeEvidenceAcrossBoundarySnapshots()
    {
        var detector = new MatchOutcomeDetector();
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

        detector.Observe(6, 300, start, confirmedSessionBoundary: false);
        detector.Observe(0, 300, start.AddSeconds(1), confirmedSessionBoundary: true);
        Assert.Equal("unknown", detector.Outcome);

        detector.Observe(0, 0, start.AddSeconds(2), confirmedSessionBoundary: true);

        Assert.Equal("fail", detector.Outcome);
        Assert.Equal("unitWipe", detector.OutcomeSource);
    }

    [Fact]
    public void Observe_DoesNotLabelAmbiguousProcessExitAsSoloFailure()
    {
        var detector = new MatchOutcomeDetector();
        var start = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

        detector.Observe(6, 0, start, confirmedSessionBoundary: false);
        detector.Observe(0, 0, start.AddSeconds(1), confirmedSessionBoundary: true);
        detector.Observe(0, 0, start.AddSeconds(2), confirmedSessionBoundary: true);

        Assert.Equal("unknown", detector.Outcome);
    }
}
