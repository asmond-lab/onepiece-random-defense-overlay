using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StandaloneScalarR2Tests
{
    [Fact]
    public void ClosingRunsAfterJournalAndCacheThenWorldIdentityAndOriginalDeadline()
    {
        var order = new List<string>();
        NativeScalarProbe.RecheckAndClose(() => order.Add("journal"), () => order.Add("cache"),
            () => StandaloneScalarRunner.CloseEvidence(() => order.Add("world"), () => order.Add("identity"),
                () => order.Add("original-deadline"), default), () => order.Add("core-final-deadline"), default);
        Assert.Equal(new[] { "journal", "cache", "world", "identity", "original-deadline", "core-final-deadline" }, order);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void FailedJournalOrCacheCannotInvokeClosing(bool journalFails)
    {
        int closes = 0;
        Assert.Throws<InvalidDataException>(() => NativeScalarProbe.RecheckAndClose(
            () => { if (journalFails) throw new InvalidDataException(); },
            () => { if (!journalFails) throw new InvalidDataException(); }, () => closes++, () => { }, default));
        Assert.Equal(0, closes);
    }
    [Fact]
    public void TwoCompleteResourceBindingsPlusPayloadReplayWorldAndHeadersFitUnchangedBudget()
    {
        const int resource = 857600, header = 4096, peb = 16, payloadAndReplay = 300000, world = 256;
        var budget = new BoundReadSession.ProbeBudget(() => TimeSpan.FromSeconds(1));
        void Identity() { budget.Charge(peb); budget.Charge(header); budget.Charge(resource); }
        Identity(); // Core preamble: first full binding.
        budget.Charge(payloadAndReplay);
        NativeScalarProbe.RecheckAndClose(() => { }, () => { },
            () => StandaloneScalarRunner.CloseEvidence(() => budget.Charge(world), Identity, budget.Check, default), budget.Check, default);
        long expected = 2L * (resource + header + peb) + payloadAndReplay + world;
        var snapshot = budget.Snapshot();
        Assert.Equal(expected, snapshot.RequestedReadBytes);
        Assert.Equal(2 * 1024 * 1024, snapshot.ByteLimit);
        Assert.Equal(5000, snapshot.DeadlineMilliseconds);
        Assert.Equal(8, snapshot.ReadCalls);
        Assert.Throws<InvalidDataException>(() => budget.Charge(resource)); // The former third resource pass cannot fit.
        Assert.Equal(expected, budget.Snapshot().RequestedReadBytes); // Rejection does not reset/exempt the budget.
    }
    [Fact]
    public void RealRemainingByteExhaustionStillFailsWithOnlyTwoBindings()
    {
        var budget = new BoundReadSession.ProbeBudget(() => TimeSpan.Zero);
        budget.Charge(857600); budget.Charge(400000);
        Assert.Throws<InvalidDataException>(() => budget.Charge(857600));
    }
    [Theory] [InlineData(4999, false)] [InlineData(5000, true)] [InlineData(5001, true)]
    public void DeadlineChecksAfterLastBindingMetadataCallIncludingExactFiveSeconds(int finalMs, bool fails)
    {
        var age = TimeSpan.FromSeconds(1);
        var budget = new BoundReadSession.ProbeBudget(() => age);
        bool reachedLastMetadata = false;
        void Close() => NativeScalarProbe.RecheckAndClose(() => { }, () => { }, () =>
            StandaloneScalarRunner.CloseEvidence(() => budget.Charge(16), () =>
            {
                budget.Charge(857600); // Read enters before deadline, then native read/final metadata finishes late.
                age = TimeSpan.FromMilliseconds(finalMs); reachedLastMetadata = true;
            }, budget.Check, default), budget.Check, default);
        if (fails) Assert.Throws<InvalidDataException>(Close); else Close();
        Assert.True(reachedLastMetadata);
        Assert.Equal(finalMs, budget.Snapshot().ElapsedMilliseconds);
    }
    [Fact]
    public void FinalCoreCheckCannotRenewOriginalLeaseDeadline()
    {
        var originalAge = TimeSpan.FromMilliseconds(4999);
        var budget = new BoundReadSession.ProbeBudget(() => originalAge);
        Assert.Throws<InvalidDataException>(() => NativeScalarProbe.RecheckAndClose(() => { }, () => { },
            () => { budget.Check(); originalAge = TimeSpan.FromSeconds(5); }, budget.Check, default));
    }
    [Fact]
    public void CancellationAtClosingDoesNotProduceCompletedOutcome()
    {
        using var cts = new CancellationTokenSource(); bool after = false;
        Assert.Throws<OperationCanceledException>(() => NativeScalarProbe.RecheckAndClose(() => { }, () => { },
            () => StandaloneScalarRunner.CloseEvidence(() => { }, cts.Cancel, () => after = true, cts.Token),
            () => after = true, cts.Token));
        Assert.False(after);
    }
    [Fact]
    public void PartialNativeReadPropagatesTypedFailureAndOuterBlockedWithBudgetAndStage()
    {
        var budget = new BoundReadSession.ProbeBudget(() => TimeSpan.FromMilliseconds(10));
        NativeScalarProbe.Result result;
        try
        {
            budget.Charge(8);
            BoundReadSession.ExactRead(0x10000, 8,
                _ => new ReadOnlyProcessMemory.ModuleRegionInfo(0x10000, 4096, 0x1000, 4, 0x20000),
                (_, _) => new byte[7], default);
            throw new Exception("Synthetic partial read unexpectedly accepted");
        }
        catch (InvalidDataException e) { result = NativeScalarProbe.Failed(e, "scalar-payload", budget.Snapshot()); }
        var start = DateTimeOffset.UtcNow;
        var report = StandaloneScalarRunner.Finish(start, start.AddSeconds(1), TimeSpan.FromSeconds(1), true, result, null);
        Assert.Equal(NativeScalarProbe.Outcome.FailedRead, result.Outcome);
        Assert.Equal("Blocked", report.Status); Assert.False(report.ProbeReadCompleted);
        Assert.Equal("scalar-payload", report.FailureStage); Assert.Equal("InvalidDataException", report.Failure);
        Assert.Equal(8, report.SharedBudget!.RequestedReadBytes);
        Assert.Null(report.DiagnosticCurrentValue); Assert.False(report.CanCoach); Assert.False(report.ExperimentalLayoutVerified);
    }
    [Fact]
    public void CompletedComparisonMismatchIsNotMisclassifiedAsFailedRead()
    {
        var start = DateTimeOffset.UtcNow;
        var probe = new NativeScalarProbe.Result(NativeScalarProbe.Outcome.BlockedComparison, "complete",
            "Stable pair disagrees with owned title", new { RawScalarPb = 7 });
        var report = StandaloneScalarRunner.Finish(start, start.AddSeconds(4), TimeSpan.FromSeconds(4), true, probe, null);
        Assert.Equal("BlockedComparison", report.Status); Assert.True(report.ProbeReadCompleted);
        Assert.True(report.HistoricalOnly); Assert.False(report.Fresh); Assert.NotNull(report.Evidence);
        Assert.False(report.GameplayReady); Assert.False(report.ExperimentalLayoutVerified);
    }
    [Fact]
    public void FailedReadStaysBlockedRatherThanExpiredAndRetainsHistoricalQualification()
    {
        var start = DateTimeOffset.UtcNow;
        var report = StandaloneScalarRunner.Finish(start, start.AddSeconds(9), TimeSpan.FromSeconds(9), true,
            NativeScalarProbe.Failed(new InvalidDataException(), "closing-world-and-binding"), null);
        Assert.Equal("Blocked", report.Status); Assert.True(report.HistoricalOnly); Assert.False(report.Fresh);
        Assert.Equal("closing-world-and-binding", report.FailureStage);
    }
    [Fact]
    public void ExactThreeSecondSourceExpiryIsIndependentOfFreshProbeClock()
    {
        var start = DateTimeOffset.UtcNow;
        var probe = new NativeScalarProbe.Result(NativeScalarProbe.Outcome.IndependentAgreement, "complete", null,
            ProbeStartedAt: start.AddMilliseconds(2999), ProbeCompletedAt: start.AddSeconds(3), ProbeElapsedMilliseconds: 1);
        var report = StandaloneScalarRunner.Finish(start, start.AddSeconds(3), TimeSpan.FromSeconds(3), true, probe, null);
        Assert.Equal(start, report.ScanStartedAt); Assert.Equal("Expired", report.Status); Assert.True(report.HistoricalOnly);
        Assert.Null(report.DiagnosticCurrentValue); Assert.False(report.ExperimentalLayoutVerified);
        var json = JsonSerializer.SerializeToElement(probe);
        Assert.True(json.TryGetProperty("ProbeStartedAt", out _)); Assert.True(json.TryGetProperty("ProbeElapsedMilliseconds", out _));
        Assert.False(json.TryGetProperty("StartedAt", out _)); Assert.False(json.TryGetProperty("ElapsedMilliseconds", out _));
    }
    [Fact]
    public void SharedReadSnapshotIsSeparateFromPayloadReplayCounters()
    {
        var budget = new BoundReadSession.ProbeBudget(() => TimeSpan.FromMilliseconds(50));
        budget.Charge(857600); budget.Charge(100); budget.Charge(857600); budget.Charge(20);
        var probe = new NativeScalarProbe.Result(NativeScalarProbe.Outcome.IndependentAgreement, "complete", null,
            PayloadRequestedReadBytes: 100, PayloadReadCalls: 1, SharedBudget: budget.Snapshot());
        Assert.Equal(100, probe.PayloadRequestedReadBytes);
        Assert.Equal(1715320, probe.SharedBudget!.RequestedReadBytes); Assert.Equal(4, probe.SharedBudget.ReadCalls);
    }
}
