using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiagnosticInventoryCadenceTests
{
    [Fact]
    public void IndependentBasicOpportunityPrecedesNextFullDeadline()
    {
        var now = TimeSpan.Zero;
        var cadence = new DiagnosticInventoryCadence(() => now);
        Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
        now = TimeSpan.FromMilliseconds(50);
        cadence.Complete();
        now = TimeSpan.FromMilliseconds(99);
        Assert.Null(cadence.TryBegin());
        now = TimeSpan.FromMilliseconds(100);
        Assert.Equal(DiagnosticReadLane.Basic, cadence.TryBegin());
        cadence.Complete();
        now = TimeSpan.FromMilliseconds(200);
        Assert.Equal(DiagnosticReadLane.Basic, cadence.TryBegin());
        cadence.Complete();
        now = TimeSpan.FromMilliseconds(250);
        Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
    }

    [Fact]
    public void FullCallsRemainBoundedAtOriginalCadence()
    {
        var now = TimeSpan.Zero;
        var cadence = new DiagnosticInventoryCadence(() => now);
        var full = new List<int>();
        var basic = new List<int>();
        for (var ms = 0; ms <= 1000; ms++)
        {
            now = TimeSpan.FromMilliseconds(ms);
            var lane = cadence.TryBegin();
            if (lane is null) continue;
            (lane == DiagnosticReadLane.Full ? full : basic).Add(ms);
            cadence.Complete();
        }
        Assert.Equal(new[] { 0, 250, 500, 750, 1000 }, full);
        Assert.Equal(new[] { 100, 200, 350, 450, 600, 700, 850, 950 }, basic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SlowReadExcludesOtherWorkAndKeepsAtMostOneDueFullWithoutCatchUp(bool slowBasic)
    {
        var now = TimeSpan.Zero;
        var cadence = new DiagnosticInventoryCadence(() => now);
        Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
        if (slowBasic)
        {
            cadence.Complete();
            now = TimeSpan.FromMilliseconds(100);
            Assert.Equal(DiagnosticReadLane.Basic, cadence.TryBegin());
        }
        now = TimeSpan.FromSeconds(5);
        Assert.Null(cadence.TryBegin());
        cadence.Complete();
        if (slowBasic)
        {
            Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
            cadence.Complete();
        }
        Assert.Null(cadence.TryBegin());
        Assert.Equal(TimeSpan.FromMilliseconds(100), cadence.NextDelay);
        now += TimeSpan.FromMilliseconds(100);
        Assert.Equal(DiagnosticReadLane.Basic, cadence.TryBegin());
        cadence.Complete();
        Assert.Null(cadence.TryBegin());
        now = TimeSpan.FromMilliseconds(5250);
        Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
    }

    [Fact]
    public void Sustained150msBasicReadsCannotStarveDueFullReads()
    {
        var now = TimeSpan.Zero;
        var cadence = new DiagnosticInventoryCadence(() => now);
        var fullStarts = new List<int>();
        for (var cycle = 0; cycle < 5; cycle++)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(cycle * 250), now);
            Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
            fullStarts.Add((int)now.TotalMilliseconds);
            now += TimeSpan.FromMilliseconds(50);
            Assert.Null(cadence.TryBegin());
            cadence.Complete();
            Assert.Null(cadence.TryBegin());
            now += TimeSpan.FromMilliseconds(50);
            Assert.Equal(DiagnosticReadLane.Basic, cadence.TryBegin());
            now += TimeSpan.FromMilliseconds(150);
            Assert.Null(cadence.TryBegin());
            cadence.Complete();
            // One current full opportunity remains due; it is not a replay queue.
        }
        Assert.Equal(new[] { 0, 250, 500, 750, 1000 }, fullStarts);
    }

    [Fact]
    public void ResetStartsNewGenerationWithFullRead()
    {
        var now = TimeSpan.Zero;
        var cadence = new DiagnosticInventoryCadence(() => now);
        Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
        cadence.Complete();
        now = TimeSpan.FromMilliseconds(10);
        cadence.Reset();
        Assert.Equal(DiagnosticReadLane.Full, cadence.TryBegin());
    }
}
