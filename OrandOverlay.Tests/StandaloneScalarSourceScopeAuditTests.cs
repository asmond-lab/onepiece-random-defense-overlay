using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StandaloneScalarSourceScopeAuditTests
{
    private static Warcraft300SourceScopeEntry Entry(string name, bool expected = true) =>
        new(Warcraft300SourceScopeAudit.HashIdentifier(name), name.Length, 4, 4, expected);
    private static Warcraft300SourceScopeAudit CompleteAudit()
    {
        var entries = Enumerable.Range(0, 3936).Select(i => Entry("expected_" + i)).Append(Entry("synthetic_other", false)).ToArray();
        return new(entries, 3936);
    }
    [Fact]
    public void AuditClonesEntriesAndCannotBeModifiedThroughInputArrayOrReturnedList()
    {
        var input = new[] { Entry("QR"), Entry("synthetic_other", false) };
        var original = input[0]; var audit = new Warcraft300SourceScopeAudit(input, 1);
        input[0] = Entry("changed", false);
        Assert.Equal(original, audit.Entries[0]); Assert.NotSame(original, audit.Entries[0]);
        var list = Assert.IsAssignableFrom<IList<Warcraft300SourceScopeEntry>>(audit.Entries);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = input[0]);
        Assert.Throws<NotSupportedException>(() => list.Add(input[0]));
        Assert.Equal(2, audit.TotalCount); Assert.Equal(1, audit.MatchedExpectedMapDeclarationCount);
    }
    [Fact]
    public void AuditRejectsOverlongIncompleteAndMalformedLocalSnapshots()
    {
        Assert.Throws<InvalidDataException>(() => new Warcraft300SourceScopeAudit(Array.Empty<Warcraft300SourceScopeEntry>(), 1));
        Assert.Throws<InvalidDataException>(() => new Warcraft300SourceScopeAudit(Enumerable.Repeat(Entry("x"), 20001).ToArray(), 20001));
        Assert.Throws<InvalidDataException>(() => new Warcraft300SourceScopeAudit(new[] { Entry("x", false) }, 1));
        Assert.Throws<InvalidDataException>(() => new Warcraft300SourceScopeAudit(new[] { Entry("x") with { RuntimeTypeTag = 14 } }, 1));
        Assert.Throws<InvalidDataException>(() => new Warcraft300SourceScopeAudit(new[] { Entry("x") with { NormalizedNameSha256 = "raw-suspect-name" } }, 1));
    }
    [Fact]
    public void ConsumerRequiresExactly3936ExpectedDeclarationsButRetainsExtraMetadata()
    {
        var complete = CompleteAudit(); Assert.Same(complete, StandaloneScalarRunner.AcceptSourceScopeAudit(complete));
        Assert.Equal(3937, complete.TotalCount); Assert.Equal(1, complete.OtherIdentifierCount);
        var tooFew = new Warcraft300SourceScopeAudit(new[] { Entry("QR") }, 1);
        Assert.Throws<InvalidDataException>(() => StandaloneScalarRunner.AcceptSourceScopeAudit(tooFew));
        Assert.Throws<InvalidDataException>(() => StandaloneScalarRunner.AcceptSourceScopeAudit(null));
    }
    [Fact]
    public void FailedGrowthCannotAttachEvenAPassedAuditAndAuditDoesNotRenewExpiryOrUnblockScalar()
    {
        var start = DateTimeOffset.UtcNow; var audit = CompleteAudit();
        var failed = NativeScalarProbe.Failed(new InvalidDataException(), "complete-source-globals");
        var growthFailed = StandaloneScalarRunner.Finish(start, start.AddSeconds(1), TimeSpan.FromSeconds(1), false,
            null, "InvalidDataException", sourceScopeAudit: audit);
        Assert.Null(growthFailed.SourceScopeAudit);
        var scalarFailed = StandaloneScalarRunner.Finish(start, start.AddSeconds(3), TimeSpan.FromSeconds(3), true,
            failed, null, sourceScopeAudit: audit);
        Assert.Same(audit, scalarFailed.SourceScopeAudit); Assert.Equal("Blocked", scalarFailed.Status);
        Assert.True(scalarFailed.HistoricalOnly); Assert.False(scalarFailed.Fresh);
        Assert.Equal(start, scalarFailed.ScanStartedAt); Assert.False(scalarFailed.ExperimentalLayoutVerified);
        Assert.False(scalarFailed.GameplayReady); Assert.False(scalarFailed.CanCoach); Assert.Null(scalarFailed.DiagnosticCurrentValue);
    }
    [Fact]
    public void AuditHashesAgreeWithMismatchNormalizationAndExportNoRawNamesPayloadsOrAddresses()
    {
        Assert.Equal(NativeScalarProbe.NormalizedIdentifierSha256("e\u0301"), Warcraft300SourceScopeAudit.HashIdentifier("\u00E9"));
        var audit = new Warcraft300SourceScopeAudit(new[] { Entry("synthetic_expected"), Entry("synthetic_other", false) }, 1);
        var json = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain("synthetic_expected", json); Assert.DoesNotContain("synthetic_other", json);
        var entryFields = typeof(Warcraft300SourceScopeEntry).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "DeclaredTypeTag", "IdentifierLength", "IsExpectedMapDeclaration", "NormalizedNameSha256", "RuntimeTypeTag" }, entryFields);
        Assert.Null(typeof(Warcraft300SourceScopeAudit).GetProperty("ExperimentalLayoutVerified"));
        Assert.Null(typeof(Warcraft300SourceScopeAudit).GetProperty("Round"));
    }
}
