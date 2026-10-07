using System;
using System.IO;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StandaloneScalarInstrumentationTests
{
    [Fact]
    public void PartialRpmKeepsFullRequestChargeAndActualReturnedCount()
    {
        long clock = 0;
        var metrics = new BoundReadSession.DiscoveryMetrics(() => clock, 1000);
        metrics.Request(8192);
        metrics.Rpm(8192, 4096, false, 299, 1, 4);
        clock = 10;
        var s = metrics.Complete();
        Assert.Equal(1, s.ReadCalls); Assert.Equal(8192, s.RequestedBytes); Assert.Equal(4096, s.ReturnedBytes);
        Assert.Equal(8192, s.RpmRequestedBytes); Assert.Equal(4096, s.RpmReturnedBytes);
        Assert.Equal(1, s.RpmFailedCalls); Assert.Equal(1, s.RpmPartialCalls); Assert.Equal(299, s.LastRpmOsError);
        Assert.Equal(1, s.LargeSizeClassAtLeast4096.Requests); Assert.Equal(4096, s.LargeSizeClassAtLeast4096.ReturnedBytes);
        Assert.Equal(0, s.SmallSizeClassBelow4096.Requests); Assert.Equal(3, s.RpmMilliseconds);
    }
    [Fact]
    public void SizeClassesAreSizeOnlyAndBoundary4096IsLarge()
    {
        var m = new BoundReadSession.DiscoveryMetrics(() => 0, 1000);
        m.Request(4095); m.Request(4096); m.Request(32);
        m.Rpm(4095, 4095, true, 0, 0, 0); m.Rpm(4096, 4096, true, 0, 0, 0);
        var s = m.Complete();
        Assert.Equal(2, s.SmallSizeClassBelow4096.Requests); Assert.Equal(4127, s.SmallSizeClassBelow4096.RequestedBytes);
        Assert.Equal(1, s.LargeSizeClassAtLeast4096.Requests); Assert.Equal(4096, s.LargeSizeClassAtLeast4096.RequestedBytes);
        Assert.Equal(3, s.ReadCalls); Assert.Equal(2, s.RpmCalls); // One request never reached RPM.
    }
    [Fact]
    public void AddressWalkAndReadValidationQueriesHaveSeparateCountsTimesAndErrors()
    {
        long tick = 100;
        var m = new BoundReadSession.DiscoveryMetrics(() => tick, 1000);
        m.Query(BoundReadSession.QueryClass.AddressWalk, true, 0, 101, 103);
        m.Query(BoundReadSession.QueryClass.AddressWalk, false, 87, 103, 106);
        m.Query(BoundReadSession.QueryClass.ReadValidation, true, 0, 106, 110);
        m.Query(BoundReadSession.QueryClass.ReadValidation, true, 0, 110, 111);
        m.Request(4); m.Rpm(4, 4, true, 0, 111, 116); tick = 130;
        var s = m.Complete();
        Assert.Equal(2, s.AddressWalkVq.Calls); Assert.Equal(5, s.AddressWalkVq.Milliseconds);
        Assert.Equal(3, s.AddressWalkVq.MaximumMilliseconds); Assert.Equal(1, s.AddressWalkVq.Failures); Assert.Equal(87, s.AddressWalkVq.LastOsError);
        Assert.Equal(2, s.ReadValidationVq.Calls); Assert.Equal(5, s.ReadValidationVq.Milliseconds);
        Assert.Equal(4, s.ReadValidationVq.MaximumMilliseconds); Assert.Equal(0, s.ReadValidationVq.Failures);
        Assert.Equal(5, s.RpmMaximumMilliseconds); Assert.Equal(15, s.NativeCallMilliseconds);
        Assert.Equal(30, s.DiscoveryElapsedMilliseconds); Assert.Equal(15, s.ElapsedMinusNativeCallsMilliseconds);
    }
    [Fact]
    public void TickConversionUsesFloatingPointFrequencyNotIntegerMilliseconds()
    {
        long tick = 900;
        var m = new BoundReadSession.DiscoveryMetrics(() => tick, 3000);
        m.Query(BoundReadSession.QueryClass.AddressWalk, true, 0, 900, 901);
        tick = 906; var s = m.Complete();
        Assert.Equal(1.0 / 3.0, s.AddressWalkVq.Milliseconds, 10);
        Assert.Equal(2, s.DiscoveryElapsedMilliseconds);
        Assert.Equal(5.0 / 3.0, s.ElapsedMinusNativeCallsMilliseconds, 10);
    }
    [Fact]
    public void CompletedSnapshotDoesNotCallClockOrTargetAndDoesNotAccumulateLaterCalls()
    {
        bool stopped = false; int clockCalls = 0, targetCalls = 0;
        var m = new BoundReadSession.DiscoveryMetrics(() =>
        { if (stopped) throw new Exception("Snapshot consulted mutable clock"); clockCalls++; return 10; }, 1000);
        m.Request(8); targetCalls++; m.Rpm(8, 8, true, 0, 0, 1);
        m.Yield(4096); var first = m.Complete(); var before = clockCalls; stopped = true;
        m.Request(999); m.Yield(8192);
        Assert.Equal(first, m.Snapshot()); Assert.Equal(before, clockCalls); Assert.Equal(1, targetCalls);
        Assert.Equal(1, first.YieldedRegions); Assert.Equal(4096, first.YieldedRegionBytes);
    }
    [Fact]
    public void InvalidAddressNeverAppearsInMetricsOrTriggersNativeDelegate()
    {
        var m = new BoundReadSession.DiscoveryMetrics(() => 0, 1000); int native = 0;
        m.Request(8);
        Assert.Throws<InvalidDataException>(() => BoundReadSession.ExactRead(ulong.MaxValue, 8,
            _ => { native++; return null; }, (_, _) => { native++; return new byte[8]; }, default));
        var snapshot = m.Complete(); Assert.Equal(0, native); Assert.Equal(1, snapshot.ReadCalls); Assert.Equal(0, snapshot.RpmCalls);
        AssertNumericObject(JsonSerializer.SerializeToElement(snapshot));
    }
    [Fact]
    public void FailureCaptureRunsOnceAndPreservesMetricsWithoutResettingExistingBudget()
    {
        long tick = 0; int starts = 0, ends = 0, reads = 0;
        var m = new BoundReadSession.DiscoveryMetrics(() => tick, 1000);
        var budget = new BoundReadSession.ProbeBudget(() => TimeSpan.Zero); budget.Charge(17);
        BoundReadSession.DiscoveryMetricsSnapshot? saved = null;
        var error = Assert.Throws<InvalidDataException>(() => StandaloneScalarRunner.CaptureDiscovery<int>(
            () => starts++, () => { m.Request(64); reads++; m.Rpm(64, 64, true, 0, 0, 1); tick = 32059; throw new InvalidDataException("Incomplete discovery: time cap."); },
            () => { ends++; return m.Complete(); }, snapshot => saved = snapshot));
        Assert.Equal(1, starts); Assert.Equal(1, ends); Assert.Equal(1, reads); Assert.Equal(17, budget.Snapshot().RequestedReadBytes);
        var start = DateTimeOffset.UtcNow;
        var report = StandaloneScalarRunner.Finish(start, start.AddMilliseconds(32059), TimeSpan.FromMilliseconds(32059), false, null,
            error.GetType().Name, "complete-growth-discovery", discoveryMetrics: saved, failureCode: StandaloneScalarRunner.ClassifyFailure(error));
        Assert.Equal("Blocked", report.Status); Assert.True(report.HistoricalOnly); Assert.False(report.DiscoveryComplete);
        Assert.Equal("InvalidDataException", report.Failure); Assert.Equal(StandaloneScalarRunner.FailureKind.DiscoveryTimeCap, report.FailureCode);
        Assert.Equal(32059, report.DiscoveryMetrics!.DiscoveryElapsedMilliseconds); Assert.False(report.ExperimentalLayoutVerified); Assert.Null(report.DiagnosticCurrentValue);
        var json = JsonSerializer.SerializeToElement(report);
        Assert.Equal("DiscoveryTimeCap", json.GetProperty("FailureCode").GetString());
        Assert.Equal(1, reads); // Formatting/snapshot/failure classification never call the target.
    }
    [Theory]
    [InlineData("Incomplete discovery: time cap.", (int)StandaloneScalarRunner.FailureKind.DiscoveryTimeCap)]
    [InlineData("Incomplete discovery: byte cap.", (int)StandaloneScalarRunner.FailureKind.DiscoveryByteCap)]
    [InlineData("Incomplete discovery: region exceeds remaining byte cap.", (int)StandaloneScalarRunner.FailureKind.DiscoveryByteCap)]
    [InlineData("Diagnostic metadata budget exceeded.", (int)StandaloneScalarRunner.FailureKind.MetadataByteCap)]
    [InlineData("Incomplete discovery: time cap. 0xDEADBEEF", (int)StandaloneScalarRunner.FailureKind.ProtocolRejected)]
    [InlineData("Incomplete discovery: short read at 0xDEADBEEF (7/8).", (int)StandaloneScalarRunner.FailureKind.ProtocolRejected)]
    [InlineData("unknown secret text", (int)StandaloneScalarRunner.FailureKind.ProtocolRejected)]
    public void FailureCodeUsesExactAllowlistNeverSubstringOrRawText(string text, int expected)
    {
        Assert.Equal((StandaloneScalarRunner.FailureKind)expected, StandaloneScalarRunner.ClassifyFailure(new InvalidDataException(text)));
    }
    [Fact]
    public void SameMessageFromUnrecognizedExceptionTypeIsNotAnApprovedProtocolCode()
    {
        Assert.Equal(StandaloneScalarRunner.FailureKind.ProtocolRejected,
            StandaloneScalarRunner.ClassifyFailure(new Exception("Incomplete discovery: time cap.")));
        Assert.Equal(StandaloneScalarRunner.FailureKind.Cancelled,
            StandaloneScalarRunner.ClassifyFailure(new OperationCanceledException("private detail")));
    }
    [Fact]
    public void CountersSaturateAndAllExportedMetricLeavesAreBoundedNumbers()
    {
        long tick = 0;
        var m = new BoundReadSession.DiscoveryMetrics(() => tick, 1);
        m.Yield(ulong.MaxValue); m.Yield(ulong.MaxValue);
        m.Request(int.MaxValue); m.Rpm(int.MaxValue, long.MaxValue, false, -5, long.MinValue, long.MaxValue);
        m.Query(BoundReadSession.QueryClass.ReadValidation, false, -1, long.MaxValue, long.MinValue);
        tick = long.MaxValue; var s = m.Complete();
        Assert.Equal(long.MaxValue, s.YieldedRegionBytes); Assert.Equal(int.MaxValue, s.ReturnedBytes);
        Assert.Equal(0, s.LastRpmOsError); Assert.Equal(0, s.ReadValidationVq.LastOsError);
        Assert.InRange(s.RpmMilliseconds, 0, 1e12); Assert.InRange(s.ElapsedMinusNativeCallsMilliseconds, 0, 1e12);
        AssertNumericObject(JsonSerializer.SerializeToElement(s));
    }
    private static void AssertNumericObject(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) AssertNumericObject(property.Value);
        else
        {
            Assert.Equal(JsonValueKind.Number, value.ValueKind);
            Assert.True(double.IsFinite(value.GetDouble())); Assert.True(value.GetDouble() >= 0);
        }
    }
}
