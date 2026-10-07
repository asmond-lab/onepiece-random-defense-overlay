using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StandaloneScalarAdapterTests
{
    private sealed class Lease : IDisposable { internal int Closes; public void Dispose() => Closes++; }
    [Fact] public void PidMismatchPreventsAllCopyAndOpenIo()
    {
        int io = 0;
        Assert.Throws<InvalidDataException>(() => BoundReadSession.Acquire(
            () => throw new InvalidDataException("PID"), () => { io++; return Array.Empty<byte>(); },
            _ => { io++; return new Lease(); }, _ => io++));
        Assert.Equal(0, io);
    }
    [Fact] public void IdentityIsRepeatedImmediatelyBeforeOpen()
    {
        var trace = new List<string>();
        using var lease = BoundReadSession.Acquire(() => trace.Add("guard"),
            () => { trace.Add("copy"); return Array.Empty<byte>(); },
            _ => { trace.Add("open"); return new Lease(); }, _ => trace.Add("bind"));
        Assert.Equal(new[] { "guard", "copy", "guard", "open", "bind" }, trace);
    }
    [Fact] public void ChangedPidAfterCopyPreventsOpen()
    {
        int guards = 0, opens = 0;
        Assert.Throws<InvalidDataException>(() => BoundReadSession.Acquire(
            () => { if (++guards == 2) throw new InvalidDataException(); }, () => Array.Empty<byte>(),
            _ => { opens++; return new Lease(); }, _ => { }));
        Assert.Equal(0, opens);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void BindingFailureOrCancellationClosesExactlyOnce(bool cancel)
    {
        var lease = new Lease();
        Assert.ThrowsAny<Exception>(() => BoundReadSession.Acquire(() => { }, () => Array.Empty<byte>(), _ => lease,
            _ => { if (cancel) throw new OperationCanceledException(); throw new InvalidDataException(); }));
        Assert.Equal(1, lease.Closes);
    }
    [Fact] public void SuccessfulLeaseRemainsOwnedUntilCallerDisposes()
    {
        var lease = BoundReadSession.Acquire(() => { }, () => Array.Empty<byte>(), _ => new Lease(), _ => { });
        Assert.Equal(0, lease.Closes); lease.Dispose(); Assert.Equal(1, lease.Closes);
    }
    [Theory]
    [InlineData(@"C:\Game\Warcraft III.exe")]
    [InlineData(@"C:\Game\..\Game\Warcraft III.exe")]
    [InlineData(@"\\server\share\copy.exe")]
    [InlineData(@"\\?\C:\copy.exe")]
    [InlineData(@"C:\copy.exe:stream")]
    [InlineData(@"C:\copy.exe.")]
    public void ForbiddenPathsFailWithoutFileIo(string copy) =>
        Assert.Throws<InvalidDataException>(() => BoundReadSession.ValidateCopyPath(copy, @"C:\Game\Warcraft III.exe"));
    [Fact] public void ExplicitDistinctLocalPathAcceptedLexically() =>
        Assert.Equal(@"C:\authorized\copy.exe", BoundReadSession.ValidateCopyPath(@"C:\authorized\copy.exe", @"C:\Game\Warcraft III.exe"));
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void AnyComparedHeaderOrResourceByteMismatchRejects(int offset)
    {
        var actual = new byte[4]; actual[offset] = 1;
        Assert.Throws<InvalidDataException>(() => BoundReadSession.EqualBytes(new byte[4], actual));
    }
    [Fact] public void PartialReadRejects()
    {
        Assert.Throws<InvalidDataException>(() => BoundReadSession.ExactRead(0x10000, 8,
            _ => new ReadOnlyProcessMemory.ModuleRegionInfo(0x10000, 4096, 0x1000, 4, 0x20000),
            (_, _) => new byte[7], default));
    }
    [Theory] [InlineData(1U)] [InlineData(0x104U)]
    public void UnreadableOrGuardPageNeverCallsRpm(uint protection)
    {
        var reads = 0;
        Assert.Throws<InvalidDataException>(() => BoundReadSession.ExactRead(0x10000, 8,
            _ => new ReadOnlyProcessMemory.ModuleRegionInfo(0x10000, 4096, 0x1000, protection, 0x20000),
            (_, _) => { reads++; return new byte[8]; }, default));
        Assert.Equal(0, reads);
    }
    [Fact] public void CancelledReadDoesNotQueryOrRead()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); int io = 0;
        Assert.Throws<OperationCanceledException>(() => BoundReadSession.ExactRead(0x10000, 8,
            _ => { io++; return null; }, (_, _) => { io++; return new byte[8]; }, cancel.Token));
        Assert.Equal(0, io);
    }
    [Theory] [InlineData("Incomplete discovery")] [InlineData("Duplicate owner aggregate")]
    public void DiscoveryFailureNeverInvokesScalarProbe(string reason)
    {
        var probes = 0;
        Assert.Throws<InvalidDataException>(() => StandaloneScalarRunner.AfterCompleteDiscovery<int>(
            () => throw new InvalidDataException(reason), _ => { probes++; return new NativeScalarProbe.Result(NativeScalarProbe.Outcome.IndependentAgreement, "complete", null); }));
        Assert.Equal(0, probes);
    }
    [Theory] [InlineData(2999, true)] [InlineData(3000, false)] [InlineData(3001, false)] [InlineData(-1, false)]
    public void FreshnessExpiresAtEquality(int milliseconds, bool fresh) =>
        Assert.Equal(fresh, StandaloneScalarRunner.IsFresh(TimeSpan.FromMilliseconds(milliseconds)));
    [Fact] public void ExpiredEvidenceRemainsHistoricalAndNeverApproves()
    {
        var start = DateTimeOffset.UtcNow;
        var result = StandaloneScalarRunner.Finish(start, start.AddSeconds(4), TimeSpan.FromSeconds(4), true,
            new NativeScalarProbe.Result(NativeScalarProbe.Outcome.IndependentAgreement, "complete", null, new { RawScalarPb = 7, RawScalarEb = 8 }), null);
        Assert.Equal(start, result.ScanStartedAt); Assert.Equal("Expired", result.Status);
        Assert.True(result.HistoricalOnly); Assert.NotNull(result.Evidence); Assert.Null(result.DiagnosticCurrentValue);
        Assert.False(result.ExperimentalLayoutVerified); Assert.False(result.GameplayReady); Assert.False(result.CanCoach);
        Assert.False(result.Verified); Assert.False(result.RuntimeWholeImageEquivalence);
        Assert.Null(result.GetType().GetProperty("Round")); Assert.Null(result.GetType().GetProperty("Rows"));
    }
    [Fact] public void FreshMatchingHypothesisStillHasUnknownLayout()
    {
        var start = DateTimeOffset.UtcNow;
        var result = StandaloneScalarRunner.Finish(start, start.AddSeconds(1), TimeSpan.FromSeconds(1), true,
            new NativeScalarProbe.Result(NativeScalarProbe.Outcome.IndependentAgreement, "complete", null, new { NativeOwnedTitleMatchesScalar = true }), null);
        Assert.Equal("UnknownLayout", result.Status); Assert.Null(result.DiagnosticCurrentValue);
        Assert.False(result.ExperimentalLayoutVerified); Assert.False(result.CanCoach);
    }
    [Theory]
    [InlineData("--probe-scalars-only")]
    [InlineData(@"--probe-scalars-only --capture-ui --authorized-exe-copy C:\copy.exe")]
    [InlineData(@"--probe-scalars-only --probe-scalars --authorized-exe-copy C:\copy.exe")]
    [InlineData(@"--authorized-exe-copy C:\copy.exe")]
    public void InvalidStandaloneFlagCombinationsFailWithoutIo(string suffix)
    {
        var baseline = new[] { "--pid", "50744", "--started-at", "2026-09-14T05:21:07.0906646Z", "--samples", "1", "--output", @"C:\new-output" };
        var args = new List<string>(baseline); args.AddRange(suffix.Split(' '));
        var program = typeof(NativeScalarProbe).Assembly.GetType("Program")!;
        var parse = program.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var error = Assert.Throws<TargetInvocationException>(() => parse.Invoke(null, new object[] { args.ToArray() }));
        Assert.IsType<ArgumentException>(error.InnerException);
    }

}
