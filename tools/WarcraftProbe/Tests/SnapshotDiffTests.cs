using System.IO;
using System.Text.Json;
using Xunit;

namespace WarcraftProbe.Tests;

public sealed class SnapshotDiffTests
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);
    private static readonly DateTimeOffset Time = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private static StructureInfo None(string status = "None") => new("", status, 0, null, null, null, null, Array.Empty<RawcodeCount>(), null, null, null, "No observation");
    private static StructureInfo Observed(int count = 2, string world = "world-1") => new("profile-test", "Observed", 1, 0, count + 1, count, 1,
        new[] { new RawcodeCount("hfoo", count) }, world, "ui-1", "vm-1", null);
    private static Snapshot Good(bool live = true, StructureInfo? structure = null)
    {
        var image = new ImageInfo("Game-copy.dll", 8192, HashA, null, 0x8664, 0x180000000, 0x10000, 0x400, 0x1000, 0,
            new[] { new SectionInfo(".text", 0x1000, 0x2000, 0x400, 0x1000, 0x60000020, HashA) },
            new[] { new AnchorInfo("NativeNameString", "NodeNames", new uint[] { 0x1100, 0x1200 }, "Found") });
        return new(1, "0.1.0", "sample-1", live ? "Live" : "Metadata", "test", Time, Time.AddSeconds(1), image,
            live ? new LiveInfo(123, Time.AddMinutes(-1), 0x10000, true, true, "Header+Resource", new[] {
                new RegionInfo(0x1000, 0x1000, 0x1000, 0x20, 0x1000000),
                new RegionInfo(0x2000, 0x1000, 0x1000, 0x01, 0x1000000) },
                new[] { new CodeCheck("bounded-code", 0x1100, 32, "Readable", HashA) }, structure ?? None(), 512, 2, 1, "Observed") : null);
    }
    private static Snapshot WithStructure(Snapshot snapshot, StructureInfo structure) => snapshot with { Live = snapshot.Live! with { Structures = structure } };
    private static void NoCounts(Comparison result) => Assert.DoesNotContain(result.Differences, d => d.Area is "Count" or "Rawcode count");

    [Fact]
    public void CoherentNoneAndUnknownFutureVersionAreValid()
    {
        var snapshot = Good();
        SnapshotDiff.Validate(snapshot);
        var result = SnapshotDiff.Compare(snapshot, snapshot);
        Assert.True(result.SameImage);
        Assert.True(result.SameProcessEpoch);
        NoCounts(result);
        Assert.Contains(result.Limitations, l => l.Contains("Observed가 아님"));
        Assert.Contains(result.Limitations, l => l.Contains("동일 파일의 반복 관측") && l.Contains("실행 중에도"));
        Assert.Contains("Unknown", ReportWriter.SnapshotMarkdown(snapshot));
        Assert.False(snapshot.GameplayReady);
        Assert.False(snapshot.AutomaticProfileApproval);
        Assert.False(snapshot.Live!.Structures.CraftabilityVerified);
    }

    [Fact]
    public void NoLiveIsUnknownNotAProcessMatchOrZero()
    {
        var snapshot = Good(false);
        var result = SnapshotDiff.Compare(snapshot, snapshot);
        Assert.True(result.SameImage);
        Assert.False(result.SameProcessEpoch);
        NoCounts(result);
        Assert.Contains("NoLive", ReportWriter.SnapshotMarkdown(snapshot));
    }

    [Theory]
    [InlineData("UnsupportedBuild")]
    [InlineData("NoData")]
    [InlineData("Expired")]
    [InlineData("None")]
    public void UnobservedStructureNeverGeneratesCountDeltas(string status)
    {
        var before = Good(structure: Observed());
        var after = WithStructure(before, None(status));
        var result = SnapshotDiff.Compare(before, after);
        NoCounts(result);
        Assert.Contains(result.Limitations, l => l.Contains(status));
        Assert.DoesNotContain("### CURRENTVIEW rawcode", ReportWriter.SnapshotMarkdown(after));
    }

    [Fact]
    public void ArbitraryNewBuildOfflineMetadataDoesNotRequireKnownGameHash()
    {
        var before = Good(false);
        var after = before with { CaptureId = "new-build", Image = before.Image with { Sha256 = HashB, FileVersion = "9.9-future", EntryPointRva = 0x2000 } };
        SnapshotDiff.Validate(after);
        var comparison = SnapshotDiff.Compare(before, after);
        Assert.False(comparison.SameImage);
        Assert.Contains(comparison.Differences, d => d.Name == "Entry point RVA");
        Assert.Contains(comparison.Differences, d => d.Name == "File version");
        NoCounts(comparison);
    }

    [Fact]
    public void ChangedImageBlocksCountsEvenWithSameWorldAndVersion()
    {
        var before = Good(structure: Observed());
        var after = WithStructure(before, Observed(4)) with { Image = before.Image with { Sha256 = HashB } };
        var result = SnapshotDiff.Compare(before, after);
        Assert.False(result.SameImage);
        Assert.True(result.SameProcessEpoch);
        NoCounts(result);
        Assert.Contains(result.Limitations, l => l.Contains("SHA 불일치"));
    }

    [Fact]
    public void SameWorldAllowsRawcodeDeltaAndBoundedAbsentCodeZero()
    {
        var before = Good(structure: Observed());
        var next = Observed(3) with { Rawcodes = new[] { new RawcodeCount("hrif", 3) } };
        var result = SnapshotDiff.Compare(before, WithStructure(before, next));
        var removed = Assert.Single(result.Differences, d => d.Area == "Rawcode count" && d.Name == "hfoo");
        Assert.Equal("2", removed.Before);
        Assert.Equal("0", removed.After);
        var added = Assert.Single(result.Differences, d => d.Area == "Rawcode count" && d.Name == "hrif");
        Assert.Equal("0", added.Before);
        Assert.Equal("3", added.After);
        Assert.Contains("CURRENTVIEW", added.Meaning);
        Assert.Contains("로컬/생존/완전", added.Meaning);
        Assert.False(result.AutomaticProfileApproval);
    }

    [Fact]
    public void ChangedWorldBlocksCountsWithoutManufacturingZeros()
    {
        var before = Good(structure: Observed());
        var result = SnapshotDiff.Compare(before, WithStructure(before, Observed(0, "world-2")));
        NoCounts(result);
        Assert.Contains(result.Limitations, l => l.Contains("WorldToken 누락/변경"));
    }

    [Theory]
    [InlineData("world-null")]
    [InlineData("vm-null")]
    [InlineData("ui-null")]
    [InlineData("view-null")]
    [InlineData("vm-change")]
    [InlineData("ui-change")]
    [InlineData("view-change")]
    public void EveryEpochCoordinateMustBePresentAndEqual(string change)
    {
        var before = Good(structure: Observed());
        var structure = Observed(4);
        structure = change switch
        {
            "world-null" => structure with { WorldToken = null },
            "vm-null" => structure with { VmToken = null },
            "ui-null" => structure with { UiToken = null },
            "view-null" => structure with { CurrentViewSlot = null },
            "vm-change" => structure with { VmToken = "new-vm" },
            "ui-change" => structure with { UiToken = "new-ui" },
            _ => structure with { CurrentViewSlot = 1 }
        };
        NoCounts(SnapshotDiff.Compare(before, WithStructure(before, structure)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProcessEpochIncludesBothPidAndStart(bool changePid)
    {
        var before = Good(structure: Observed());
        var after = WithStructure(before, Observed(4));
        after = after with { Live = changePid ? after.Live! with { ProcessId = 124 } : after.Live! with { ProcessStartedAt = Time } };
        var result = SnapshotDiff.Compare(before, after);
        Assert.False(result.SameProcessEpoch);
        NoCounts(result);
    }

    [Fact]
    public void AllAnchorRvasAreComparedByKindAndNameNotFirstMatchOrOrder()
    {
        var before = Good(false);
        var anchor = before.Image.Anchors[0];
        var reordered = before with { Image = before.Image with { Anchors = new[] { anchor with { Rvas = new uint[] { 0x1200, 0x1100 } } } } };
        Assert.Empty(SnapshotDiff.Compare(before, reordered).Differences);
        var moved = before with { Image = before.Image with { Anchors = new[] { anchor with { Rvas = new uint[] { 0x1100, 0x1300 } } } } };
        var difference = Assert.Single(SnapshotDiff.Compare(before, moved).Differences, d => d.Area == "Anchor RVA");
        Assert.Contains("0x00001300", difference.After);
        Assert.Contains("문자열", difference.Meaning);
        var function = before with { Image = before.Image with { Anchors = new[] { anchor with { Kind = "Function" } } } };
        Assert.Equal(2, SnapshotDiff.Compare(before, function).Differences.Count(d => d.Area == "Anchor RVA"));
    }

    [Fact]
    public void DuplicateAnchorKeysAndSectionNamesAreExplicitlyAmbiguous()
    {
        var before = Good(false);
        var section = before.Image.Sections[0];
        var after = before with { Image = before.Image with {
            Sections = new[] { section, section with { Rva = 0x4000 } },
            Anchors = new[] { before.Image.Anchors[0], before.Image.Anchors[0] with { Rvas = new uint[] { 0x1500 } } } } };
        var result = SnapshotDiff.Compare(before, after);
        Assert.Contains(result.Limitations, l => l.Contains("중복 섹션"));
        Assert.Contains(result.Limitations, l => l.Contains("중복 앵커"));
        Assert.Contains(result.Differences, d => d.Area == "Section ambiguity" && d.After.Contains("0x00004000"));
        Assert.DoesNotContain(result.Differences, d => d.Area == "Section RVA");
    }

    [Fact]
    public void SectionAndCodeAndProtectionChangesAreReported()
    {
        var before = Good();
        var after = before with { Image = before.Image with { Sections = new[] { before.Image.Sections[0] with { Rva = 0x4000, VirtualSize = 0x1000, Sha256 = HashB, Characteristics = 0x40000040 } } },
            Live = before.Live! with { CodeChecks = new[] { before.Live.CodeChecks[0] with { Status = "Unreadable", Sha256 = null } },
                Regions = new[] { new RegionInfo(0x1000, 0x1000, 0x1000, 0x04, 0x1000000) } } };
        var result = SnapshotDiff.Compare(before, after);
        foreach (var area in new[] { "Section RVA", "Section size", "Section hash", "Section flags", "Code check", "Region distribution" })
            Assert.Contains(result.Differences, d => d.Area == area);
        var markdown = ReportWriter.SnapshotMarkdown(before);
        Assert.Contains("Readable", markdown);
        Assert.Contains("NoAccess", markdown);
        Assert.Contains("Readability", markdown);
    }

    [Fact]
    public void LongRvaListsKeepFullComparisonButPreviewDeclaresTotal()
    {
        var before = Good(false);
        var values = Enumerable.Range(0, 40).Select(i => (uint)(0x2000 + i * 4)).ToArray();
        var after = before with { Image = before.Image with { Anchors = new[] { before.Image.Anchors[0] with { Rvas = values } } } };
        var result = SnapshotDiff.Compare(before, after);
        Assert.Contains("0x0000209C", Assert.Single(result.Differences, d => d.Area == "Anchor RVA").After);
        var markdown = ReportWriter.ComparisonMarkdown(result);
        Assert.Contains("앞 16개", markdown);
        Assert.Contains("총 40 RVA", markdown);
        Assert.DoesNotContain("0x0000209C", markdown);
        Assert.Contains("총 40 RVA", ReportWriter.SnapshotMarkdown(after));
    }

    [Fact]
    public void MarkdownLabelsNamesValuesCannotInjectTablesHtmlLinksOrFences()
    {
        const string hostile = "[click](https://evil.test)|<img src=x>``` &copy;";
        var before = Good(false) with { Label = hostile };
        before = before with { Image = before.Image with { FileVersion = hostile, Anchors = new[] { before.Image.Anchors[0] with { Name = hostile } } } };
        var markdown = ReportWriter.SnapshotMarkdown(before);
        Assert.DoesNotContain("<img", markdown);
        Assert.DoesNotContain("[click]", markdown);
        Assert.DoesNotContain("https://", markdown);
        Assert.Contains(@"\[click\]", markdown);
        Assert.Contains(@"\|", markdown);
        Assert.Contains("&lt;img", markdown);
        var comparison = new Comparison(1, hostile, "after", hostile, "future", false, false,
            new[] { new Difference(hostile, hostile, hostile, hostile, hostile) }, new[] { hostile });
        var report = ReportWriter.ComparisonMarkdown(comparison);
        Assert.DoesNotContain("<img", report);
        Assert.DoesNotContain("https://", report);
        Assert.DoesNotContain("```", report);
    }

    public static IEnumerable<object[]> InvalidSnapshots()
    {
        var s = Good();
        yield return new object[] { s with { SchemaVersion = 2 } };
        yield return new object[] { s with { Image = null! } };
        yield return new object[] { s with { CaptureId = null! } };
        yield return new object[] { s with { CaptureId = "bad\nID" } };
        yield return new object[] { s with { Label = "bad\u202elabel" } };
        yield return new object[] { s with { ToolVersion = null! } };
        yield return new object[] { s with { FinishedAt = Time.AddSeconds(-1) } };
        yield return new object[] { s with { Image = s.Image with { Sha256 = new string('a', 32) } } };
        yield return new object[] { s with { Image = s.Image with { Sha256 = new string('g', 64) } } };
        yield return new object[] { s with { Image = s.Image with { FileSize = -1 } } };
        yield return new object[] { s with { Image = s.Image with { Sections = null! } } };
        yield return new object[] { s with { Image = s.Image with { Sections = new SectionInfo[] { null! } } } };
        yield return new object[] { s with { Image = s.Image with { Sections = Enumerable.Repeat(s.Image.Sections[0], 97).ToArray() } } };
        yield return new object[] { s with { Image = s.Image with { EntryPointRva = s.Image.ImageSize } } };
        yield return new object[] { s with { Image = s.Image with { Anchors = null! } } };
        yield return new object[] { s with { Image = s.Image with { Anchors = new AnchorInfo[] { null! } } } };
        yield return new object[] { s with { Image = s.Image with { Anchors = Enumerable.Repeat(s.Image.Anchors[0], 1001).ToArray() } } };
        foreach (var rvas in new uint[][] { null!, new uint[] { 0x10000 }, new uint[] { 1, 1 }, Enumerable.Range(1, 257).Select(i => (uint)i).ToArray() })
            yield return new object[] { s with { Image = s.Image with { Anchors = new[] { s.Image.Anchors[0] with { Rvas = rvas } } } } };
        yield return new object[] { s with { Live = s.Live! with { Structures = null! } } };
        yield return new object[] { s with { Live = s.Live! with { Regions = null! } } };
        yield return new object[] { s with { Live = s.Live! with { Regions = new RegionInfo[] { null! } } } };
        yield return new object[] { s with { Live = s.Live! with { Regions = Enumerable.Repeat(s.Live.Regions[0], 50001).ToArray() } } };
        yield return new object[] { s with { Live = s.Live! with { Regions = new[] { new RegionInfo(0xffff, ulong.MaxValue, 0, 0, 0) } } } };
        yield return new object[] { s with { Live = s.Live! with { CodeChecks = new CodeCheck[] { null! } } } };
        yield return new object[] { s with { Live = s.Live! with { ElapsedMilliseconds = double.NaN } } };
        yield return new object[] { s with { Live = s.Live! with { RequestedBytes = -1 } } };
        foreach (var structure in new[] { None() with { Rawcodes = null! }, None() with { Rawcodes = new RawcodeCount[] { null! } },
            None() with { Rawcodes = new[] { new RawcodeCount("x", -1) } }, None() with { Rawcodes = new[] { new RawcodeCount("x", 32769) } },
            None() with { Rawcodes = new[] { new RawcodeCount("x", 1), new RawcodeCount("x", 2) } },
            None() with { CurrentViewSlot = -1 }, None() with { ObservedObjectCount = 32769 }, None() with { Status = null! },
            None() with { WorldToken = "" }, Observed() with { CurrentViewObjectCount = null }, Observed() with { ObservedObjectCount = 99 }, Observed() with { Rawcodes = Array.Empty<RawcodeCount>() } })
            yield return new object[] { WithStructure(s, structure) };
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshots))]
    public void MalformedJsonShapedObjectsFailAsInvalidDataNotNullReference(Snapshot invalid)
    {
        Assert.Throws<InvalidDataException>(() => SnapshotDiff.Validate(invalid));
        Assert.Throws<InvalidDataException>(() => SnapshotDiff.Compare(Good(), invalid));
        Assert.Throws<InvalidDataException>(() => ReportWriter.SnapshotMarkdown(invalid));
    }

    [Fact]
    public void DeserializedMissingModelFieldsAndNullRootAreRejected()
    {
        var missing = JsonSerializer.Deserialize<Snapshot>("{}");
        Assert.Throws<InvalidDataException>(() => SnapshotDiff.Validate(missing!));
        Assert.Throws<InvalidDataException>(() => SnapshotDiff.Validate(null!));
        var valid = Good(false);
        var broken = JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(valid).Replace("\"Anchors\":[", "\"Anchors\":null,\"Ignored\":["));
        Assert.Throws<InvalidDataException>(() => SnapshotDiff.Validate(broken!));
    }
}
