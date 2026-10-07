using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StandaloneScalarMismatchTests
{
    private static byte[] Metadata(uint runtime = 7, uint declared = 7)
    {
        var bytes = new byte[32];
        BitConverter.GetBytes(runtime).CopyTo(bytes, 24); BitConverter.GetBytes(declared).CopyTo(bytes, 28);
        return bytes;
    }
    [Fact]
    public void IdentifierHashIsNfcUtf8Sha256WithNoCaseFoldingOrTrimming()
    {
        const string abc = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        Assert.Equal(abc, NativeScalarProbe.NormalizedIdentifierSha256("abc"));
        Assert.True(NativeScalarProbe.IdentifierHashMatches("abc", abc.ToLowerInvariant()));
        Assert.False(NativeScalarProbe.IdentifierHashMatches("ABC", abc));
        Assert.False(NativeScalarProbe.IdentifierHashMatches(" abc", abc));
        Assert.False(NativeScalarProbe.IdentifierHashMatches("abc", "invalid"));
        Assert.False(NativeScalarProbe.IdentifierHashMatches("abc", new string('G', 64)));
        Assert.Equal(NativeScalarProbe.NormalizedIdentifierSha256("e\u0301"), NativeScalarProbe.NormalizedIdentifierSha256("\u00E9"));
    }
    [Fact]
    public void MissingAndDuplicateNamesHaveDistinctFailureKindsWithoutAcceptingEither()
    {
        Assert.Equal(NativeScalarProbe.IdentifierMismatchKind.UnknownSourceIdentifier, NativeScalarProbe.ClassifyIdentifier(false, false));
        Assert.Equal(NativeScalarProbe.IdentifierMismatchKind.DuplicateSourceIdentifier, NativeScalarProbe.ClassifyIdentifier(true, true));
        Assert.Null(NativeScalarProbe.ClassifyIdentifier(true, false));
    }
    [Fact]
    public void DescriptorMatchesGrowthOffsetsAndDoesNotIncludeNodePayload()
    {
        var fullNode = new byte[64];
        BitConverter.GetBytes(0x10000000UL).CopyTo(fullNode, 40);
        BitConverter.GetBytes(4U).CopyTo(fullNode, 48); BitConverter.GetBytes(7U).CopyTo(fullNode, 52);
        BitConverter.GetBytes(0xDEADBEEFU).CopyTo(fullNode, 56);
        var metadata = fullNode.AsSpan(24, 32).ToArray();
        Assert.Equal(BitConverter.ToUInt64(fullNode, 40), BitConverter.ToUInt64(metadata, 16));
        var descriptor = NativeScalarProbe.DescribeMismatch(NativeScalarProbe.IdentifierMismatchKind.UnknownSourceIdentifier,
            "synthetic_private_identifier", metadata, 3);
        Assert.Equal(4U, descriptor.RuntimeTypeTag); Assert.Equal(7U, descriptor.DeclaredTypeTag);
        Assert.Equal(3, descriptor.EarlierExpectedMatches); Assert.Equal(28, descriptor.IdentifierLength);
        Assert.False(descriptor.BracketVerified);
        var json = JsonSerializer.Serialize(descriptor);
        Assert.DoesNotContain("synthetic_private_identifier", json); Assert.DoesNotContain("DEADBEEF", json);
        Assert.DoesNotContain("Address", json); Assert.DoesNotContain("Payload", json);
        Assert.True(NativeScalarProbe.IdentifierHashMatches("synthetic_private_identifier", descriptor.NormalizedNameSha256));
    }
    [Fact]
    public void VerificationRequiresReplayCacheWorldBindingAndDeadlineAndReturnsImmutableCopy()
    {
        var initial = NativeScalarProbe.DescribeMismatch(NativeScalarProbe.IdentifierMismatchKind.UnknownSourceIdentifier,
            "synthetic_name", Metadata(), 1);
        var order = new System.Collections.Generic.List<string>();
        var verified = NativeScalarProbe.VerifyMismatch(initial, () => NativeScalarProbe.RecheckAndClose(
            () => { order.Add("name-replay"); order.Add("metadata-replay"); }, () => order.Add("cache"),
            () => StandaloneScalarRunner.CloseEvidence(() => order.Add("world"), () => order.Add("binding"),
                () => order.Add("original-deadline"), default), () => order.Add("core-deadline"), default));
        Assert.Equal(new[] { "name-replay", "metadata-replay", "cache", "world", "binding", "original-deadline", "core-deadline" }, order);
        Assert.False(initial.BracketVerified); Assert.True(verified.BracketVerified);
        Assert.Equal(initial.NormalizedNameSha256, verified.NormalizedNameSha256);
    }
    [Theory] [InlineData("name")] [InlineData("metadata")] [InlineData("cache")]
    [InlineData("world")] [InlineData("binding")] [InlineData("deadline")]
    public void AnyFailedBracketLeavesOriginalDescriptorUnverified(string failAt)
    {
        var initial = NativeScalarProbe.DescribeMismatch(NativeScalarProbe.IdentifierMismatchKind.DuplicateSourceIdentifier,
            "pb", Metadata(4, 4), 2);
        void Check(string step) { if (step == failAt) throw new InvalidDataException(); }
        Assert.Throws<InvalidDataException>(() => NativeScalarProbe.VerifyMismatch(initial, () => NativeScalarProbe.RecheckAndClose(
            () => { Check("name"); Check("metadata"); }, () => Check("cache"),
            () => StandaloneScalarRunner.CloseEvidence(() => Check("world"), () => Check("binding"), () => Check("deadline"), default),
            () => Check("deadline"), default)));
        Assert.False(initial.BracketVerified);
    }
    [Fact]
    public void MismatchClosingConsumesOriginalBudgetAndCannotVerifyAfterItsDeadline()
    {
        var age = TimeSpan.FromMilliseconds(4999); var budget = new BoundReadSession.ProbeBudget(() => age);
        budget.Charge(857600); budget.Charge(362);
        var initial = NativeScalarProbe.DescribeMismatch(NativeScalarProbe.IdentifierMismatchKind.UnknownSourceIdentifier,
            "synthetic_name", Metadata(), 0);
        Assert.Throws<InvalidDataException>(() => NativeScalarProbe.VerifyMismatch(initial, () => NativeScalarProbe.RecheckAndClose(
            () => budget.Charge(362), () => { },
            () => StandaloneScalarRunner.CloseEvidence(() => budget.Charge(16),
                () => { budget.Charge(857600); age = TimeSpan.FromSeconds(5); }, budget.Check, default), budget.Check, default)));
        Assert.False(initial.BracketVerified); Assert.Equal(1715940, budget.Snapshot().RequestedReadBytes);
    }
    [Fact]
    public void VerifiedMismatchStillProducesBlockedNoPayloadAndNeverRenewsSourceFreshness()
    {
        var initial = NativeScalarProbe.DescribeMismatch(NativeScalarProbe.IdentifierMismatchKind.UnknownSourceIdentifier,
            "synthetic_name", Metadata(), 0);
        var verified = NativeScalarProbe.VerifyMismatch(initial, () => { });
        var started = DateTimeOffset.UtcNow;
        var failure = new NativeScalarProbe.Result(NativeScalarProbe.Outcome.FailedRead, "complete-source-globals",
            verified.Kind.ToString(), ProbeStartedAt: started.AddMilliseconds(2999), ProbeElapsedMilliseconds: 1, Mismatch: verified);
        var report = StandaloneScalarRunner.Finish(started, started.AddSeconds(3), TimeSpan.FromSeconds(3), true, failure, null);
        Assert.Equal("Blocked", report.Status); Assert.True(report.HistoricalOnly); Assert.False(report.Fresh);
        Assert.Equal("UnknownSourceIdentifier", report.Failure); Assert.Null(failure.Evidence);
        Assert.Null(report.DiagnosticCurrentValue); Assert.False(failure.ExperimentalLayoutVerified); Assert.False(failure.CanCoach);
        Assert.False(report.GameplayReady); Assert.Equal(started, report.ScanStartedAt);
    }
    [Fact]
    public void ResultConstructorRemainsCompatibleAndMismatchHasNoRawIdentifierField()
    {
        var legacyConstruction = new NativeScalarProbe.Result(NativeScalarProbe.Outcome.FailedRead, "test", "test");
        Assert.Null(legacyConstruction.Mismatch);
        var fields = typeof(NativeScalarProbe.SourceIdentifierMismatch).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "BracketVerified", "DeclaredTypeTag", "EarlierExpectedMatches", "IdentifierLength", "Kind", "NormalizedNameSha256", "RuntimeTypeTag", "VerificationScope" }, fields);
    }
}
