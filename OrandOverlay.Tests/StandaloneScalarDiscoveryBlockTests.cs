using System;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StandaloneScalarDiscoveryBlockTests
{
    [Theory]
    [InlineData(2999, true, "UnknownLayout")]
    [InlineData(3000, false, "Expired")]
    [InlineData(3500, false, "Expired")]
    public void FourMiBSelectionDoesNotExtendOriginalScanStartExpiry(int milliseconds, bool fresh, string status)
    {
        Assert.Equal(4 * 1024 * 1024, StandaloneScalarRunner.DiscoveryBlockBytes);
        Assert.Equal(64 * 1024, Warcraft300GrowthReader.DefaultDiscoveryBlockBytes);
        var started = new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero);
        var probe = new NativeScalarProbe.Result(NativeScalarProbe.Outcome.IndependentAgreement, "complete", null,
            new { RawScalarPb = 7, RawScalarEb = 8 }, ProbeStartedAt: started.AddMilliseconds(milliseconds - 1),
            ProbeCompletedAt: started.AddMilliseconds(milliseconds), ProbeElapsedMilliseconds: 1);
        var report = StandaloneScalarRunner.Finish(started, started.AddMilliseconds(milliseconds),
            TimeSpan.FromMilliseconds(milliseconds), true, probe, null);
        Assert.Equal(status, report.Status); Assert.Equal(fresh, report.Fresh); Assert.Equal(!fresh, report.HistoricalOnly);
        Assert.Equal(started, report.ScanStartedAt); Assert.Same(probe, report.Evidence);
        Assert.Null(report.DiagnosticCurrentValue); Assert.False(report.ExperimentalLayoutVerified);
        Assert.False(report.GameplayReady); Assert.False(report.CanCoach);
    }
}
