using System.Numerics;
using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Warcraft300GrowthReaderTests
{
    [Fact]
    public void CanonicalAssociationIsDiagnosticOnlyAndReadsNativeIdentityTwice()
    {
        var m = new Memory();
        var result = m.Read();
        Assert.Equal(Memory.Unit, result.UnitPointer);
        Assert.Equal(0x48303031U, result.Rawcode);
        Assert.Equal(27U, BitConverter.ToUInt32(m.Bytes(Memory.Unit + 0x1C0, 4)));
        Assert.NotNull(result.Allocation);
        Assert.Equal(m.View, result.CurrentView);
        Assert.Equal(Memory.Root, result.Root);
        Assert.Equal(Memory.Aggregate, result.OwnerAggregate);
        Assert.Contains("diagnostic only", result.StatusDescription);
        Assert.Equal(2, m.Calls.Count(c => c == (Memory.Unit + 0x18, 4)));
        // Aggregate begins in the last eight bytes of a chunk; its header crosses it.
        Assert.Contains((Memory.Aggregate, 24), m.Calls);
    }

    [Fact]
    public void ExplicitZeroIsAbsentAndDoesNotCarryOldGrowth()
    {
        var m = new Memory(); Assert.NotNull(m.Read().UnitPointer);
        m.Put32(Memory.ArrayData, 0);
        m.Calls.Clear();
        var absent = m.Read();
        Assert.Null(absent.UnitPointer); Assert.Null(absent.Rawcode); Assert.Null(absent.Allocation);
        Assert.Equal(0U, absent.JassHandle); Assert.Contains("Absent", absent.StatusDescription);
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.Unit);
        Assert.Equal(2, m.Enumerations);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("wrong-vtable")]
    [InlineData("empty")]
    [InlineData("count")]
    [InlineData("capacity-low")]
    [InlineData("capacity-high")]
    [InlineData("data-null")]
    [InlineData("out-of-bounds")]
    public void UnobservedArrayNeverBecomesAbsence(string problem)
    {
        var m = new Memory(); m.Put32(Memory.ArrayData, 0);
        switch (problem)
        {
            case "null": m.Put64(Memory.Qr + 56, 0); break;
            case "wrong-vtable": m.Put64(Memory.Array, Memory.Module); break;
            case "empty": m.Put32(Memory.Array + 8, 0); break;
            case "count": m.Put32(Memory.Array + 8, 5); break;
            case "capacity-low": m.Put32(Memory.Array + 24, 0); break;
            case "capacity-high": m.Put32(Memory.Array + 24, 33); break;
            case "data-null": m.Put64(Memory.Array + 16, 0); break;
            case "out-of-bounds": m.SetView(1); break;
        }
        Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Theory]
    [InlineData("world")]
    [InlineData("ui")]
    [InlineData("instance")]
    [InlineData("script")]
    [InlineData("table")]
    [InlineData("table-size")]
    [InlineData("tag")]
    [InlineData("declared-tag")]
    [InlineData("extra-tag")]
    [InlineData("name")]
    [InlineData("case")]
    [InlineData("duplicate")]
    [InlineData("backlink")]
    [InlineData("tail")]
    [InlineData("sentinel")]
    [InlineData("cycle")]
    [InlineData("unit-vtable")]
    [InlineData("owner")]
    [InlineData("raw")]
    [InlineData("handle")]
    [InlineData("limit")]
    [InlineData("refcount")]
    [InlineData("native-generation")]
    public void RejectsWrongStructuralAndUnitInputs(string problem)
    {
        var m = new Memory();
        switch (problem)
        {
            case "world": m.Put64(Memory.World, Memory.Module); break;
            case "ui": m.Put64(Memory.World + 0x40, Memory.Ui + 8); break;
            case "instance": m.Put64(Memory.Instance, Memory.Module); break;
            case "script": m.Put64(Memory.Script, Memory.Module); break;
            case "table": m.Put64(Memory.Table, Memory.Module); break;
            case "table-size": m.Put32(Memory.Table + 8, 23); break;
            case "tag": m.Put32(Memory.Qr + 48, 7); break;
            case "declared-tag": m.Put32(Memory.Qr + 52, 7); break;
            case "extra-tag": m.Put32(Memory.Extra + 52, 14); break;
            case "name": m.Name(Memory.QrName, "bad-name"); break;
            case "case": m.Name(Memory.QrName, "qr"); break;
            case "duplicate": m.Name(Memory.ExtraName, "QR"); break;
            case "backlink": m.Put64(Memory.Extra + 24, Memory.Qr); break;
            case "tail": m.Put64(Memory.Table + 16, Memory.Qr + 24); break;
            case "sentinel": m.Put64(Memory.Extra + 32, Memory.Table + 16); break;
            case "cycle": m.Put64(Memory.Extra + 32, Memory.Qr); break;
            case "unit-vtable": m.Put64(Memory.Unit, Memory.Module); break;
            case "owner": m.Put32(Memory.Unit + 0x1C0, 0); break;
            case "raw": m.Put32(Memory.Unit + 0x178, 0); break;
            case "handle": m.Put32(Memory.ArrayData, 1); break;
            case "limit": m.Put32(Memory.Manager + 0x290, 1048577); break;
            case "refcount": m.Put32(Memory.JassTable, 0); break;
            case "native-generation": m.Put32(Memory.Record + 0x24, 6); break;
        }
        Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Fact]
    public void ViewMustMatchAndOnlyFourUserSlotsAreAccepted()
    {
        var m = new Memory();
        Assert.Throws<InvalidDataException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module,
            new(Memory.Root, 1, Memory.Player), Memory.World, "s", m.Expected));
        m.SetView(4); Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Fact]
    public void SourceFingerprintRequiresEveryNameAndBothTagsButAllowsUninitializedExtras()
    {
        var m = new Memory(); Assert.NotNull(m.Read().UnitPointer); // extra tags 0/7 differ intentionally
        m.Expected["missing"] = 4; Assert.Throws<InvalidDataException>(() => m.Read());
        m.Expected.Remove("missing"); m.Expected["extra"] = 7;
        Assert.Throws<InvalidDataException>(() => m.Read());
        m.Expected.Clear(); Assert.Throws<InvalidDataException>(() => m.Read());
        m.Expected["QR"] = 7; Assert.Throws<InvalidDataException>(() => m.Read());
        m.Expected.Clear(); m.Expected["extra"] = 7; Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Fact]
    public void DiscoveryRejectsAmbiguousOwnersAndNeverStopsAtFirstMatch()
    {
        var m = new Memory();
        m.Put64(Memory.ScanBase, Memory.Instance); m.Put64(Memory.ScanBase + 8, Memory.Module + 0x13F3070);
        m.Put64(Memory.ScanBase + 16, Memory.Table);
        Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Theory]
    [InlineData("instance-pointer")]
    [InlineData("instance-header")]
    [InlineData("script-header")]
    [InlineData("native-generation")]
    [InlineData("array-slot")]
    [InlineData("name")]
    [InlineData("link")]
    [InlineData("jass-entry")]
    [InlineData("manager")]
    [InlineData("registry")]
    public void MidReadChangesFailClosed(string change)
    {
        var m = new Memory(); var hits = 0;
        var trigger = change == "native-generation" ? Memory.Unit + 0x18 : Memory.Qr + 24;
        m.BeforeRead = (a, n) =>
        {
            if (a != trigger) return;
            if (change == "native-generation" && ++hits != 2) return;
            m.BeforeRead = null;
            switch (change)
            {
                case "instance-pointer": m.Put64(Memory.Root + 0x25D0, Memory.Script); break;
                case "instance-header": m.Put32(Memory.Instance + 8, 2); break;
                case "script-header": m.Put32(Memory.Script + 8, 2); break;
                case "native-generation": m.Put32(Memory.Unit + 0x1C, 6); m.Put32(Memory.Record + 0x24, 6); break;
                case "array-slot": m.Put32(Memory.ArrayData, 0); break;
                case "name": m.Name(Memory.QrName, "QS"); break;
                case "link": m.Put64(Memory.Qr + 32, Memory.Table + 17); break;
                case "jass-entry": m.Put32(Memory.JassTable, 2); break;
                case "manager": m.Put64(Memory.Root + 0x2620, Memory.Manager + 8); break;
                case "registry": m.Put64(Memory.Module + 0x2F807F0, Memory.Registry + 8); break;
            }
        };
        Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Null(m.BeforeRead);
    }

    [Fact]
    public void IrrelevantScalarPayloadAndArrayPaddingMayChange()
    {
        var m = new Memory();
        m.BeforeRead = (a, n) =>
        {
            if (a != Memory.Qr + 24) return;
            m.Put64(Memory.Extra + 56, 999); m.Put32(Memory.Array + 12, 999);
            m.Put32(Memory.Array + 28, 999); m.BeforeRead = null;
        };
        Assert.NotNull(m.Read().UnitPointer);
    }

    [Fact]
    public void CacheRevalidatesNamesAndInvalidatesAfterFailureOrRootHeaderChange()
    {
        var m = new Memory(); m.Read(); m.Read(); Assert.Equal(2, m.Enumerations);
        m.Name(Memory.QrName, "QS"); Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Equal(3, m.Enumerations);
        m.Name(Memory.QrName, "QR"); m.Read(); Assert.Equal(4, m.Enumerations);
        m.Put32(Memory.Instance + 8, 2); m.Read(); Assert.Equal(5, m.Enumerations);
        m.Put32(Memory.Script + 8, 2); m.Read(); Assert.Equal(6, m.Enumerations);
        m.Session = "new-session"; m.Read(); Assert.Equal(7, m.Enumerations);
        m.Reader.Reset(); m.Read(); Assert.Equal(8, m.Enumerations);
    }

    [Fact]
    public void CurrentInstanceReplacementCannotReuseOldAggregate()
    {
        var m = new Memory(); m.Read();
        m.Allocate(0xB00000, 48); m.Put64(0xB00000, Memory.Module + 0x27ECBC0);
        m.Put64(Memory.Root + 0x25D0, 0xB00000);
        Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Equal(2, m.Enumerations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortReadsNeverBecomeNoGrowth(bool scan)
    {
        var m = new Memory(); m.ShortAt = scan ? Memory.ScanBase : Memory.QrName;
        Assert.Throws<InvalidDataException>(() => m.Read());
        m.ShortAt = null; Assert.NotNull(m.Read().UnitPointer);
        Assert.Equal(2, m.Enumerations);
    }

    [Fact]
    public void CancellationIsPropagatedIncludingAfterScanRead()
    {
        var m = new Memory(); using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => m.Read(source.Token)); Assert.Empty(m.Calls);
        using var mid = new CancellationTokenSource();
        m.BeforeRead = (a, n) => { if (a == Memory.ScanBase) mid.Cancel(); };
        Assert.Throws<OperationCanceledException>(() => m.Read(mid.Token));
    }

    [Fact]
    public void OverflowAndIncompleteIteratorAreErrors()
    {
        var m = new Memory();
        Assert.Throws<InvalidDataException>(() => m.Reader.Read(m.Bytes, m.Regions, ulong.MaxValue,
            m.View, Memory.World, "s", m.Expected));
        m.RegionOverride = () => new[] { new MemoryRegion(Memory.ScanBase, ulong.MaxValue) };
        Assert.Throws<InvalidDataException>(() => m.Read());
        m.RegionOverride = Broken;
        Assert.Throws<IOException>(() => m.Read());
        static IEnumerable<MemoryRegion> Broken()
        {
            yield return new MemoryRegion(Memory.ScanBase, 0x10030);
            throw new IOException("Enumeration incomplete");
        }
    }

    [Fact]
    public void NamesHaveStrictAsciiAndTerminatorBounds()
    {
        var m = new Memory(); m.Name(Memory.ExtraName, new string('a', 255));
        Assert.NotNull(m.Read().UnitPointer);
        m.Name(Memory.ExtraName, new string('a', 256));
        Assert.Throws<InvalidDataException>(() => m.Read());
        m.Name(Memory.ExtraName, "0bad"); Assert.Throws<InvalidDataException>(() => m.Read());
        m.Name(Memory.ExtraName, ""); Assert.Throws<InvalidDataException>(() => m.Read());
        m.Put(Memory.ExtraName, new byte[] { 0xFF, 0 }); Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Fact]
    public void DiscoveryByteCapAndCandidateCapAreFailures()
    {
        var m = new Memory();
        m.RegionOverride = () => new[] { new MemoryRegion(Memory.ScanBase, 8UL * 1024 * 1024 * 1024 + 1) };
        Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.ScanBase);
        m.RegionOverride = null;
        for (ulong i = 0; i < 17; i++)
        {
            var a = Memory.ScanBase + i * 24;
            m.Put64(a, Memory.Instance); m.Put64(a + 8, Memory.Module + 0x13F3070); m.Put64(a + 16, Memory.Table);
        }
        Assert.Throws<InvalidDataException>(() => m.Read());
    }

    [Fact]
    public void CachedStructuralHeaderChangesFailThenForceFreshDiscovery()
    {
        var m = new Memory(); m.Read();
        m.Put32(Memory.Table + 64, 1);
        Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Equal(2, m.Enumerations);
        Assert.NotNull(m.Read().UnitPointer); Assert.Equal(3, m.Enumerations);
        m.Put64(Memory.Aggregate + 8, Memory.Module);
        Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Equal(4, m.Enumerations);
        m.Put64(Memory.Aggregate + 8, Memory.Module + 0x13F3070);
        Assert.NotNull(m.Read().UnitPointer); Assert.Equal(5, m.Enumerations);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EveryReadRediscoversOwnerUniquenessEvenWithCachedIdentityAndZeroSlot(bool differentTable, bool zeroSlot)
    {
        var m = new Memory(); Assert.NotNull(m.Read().UnitPointer);
        if (zeroSlot) m.Put32(Memory.ArrayData, 0);
        var secondTable = Memory.Table;
        if (differentTable)
        {
            secondTable = 0xB20000;
            m.Allocate(secondTable, 72);
            m.Put(secondTable, m.Bytes(Memory.Table, 72));
        }
        m.Put64(Memory.ScanBase, Memory.Instance);
        m.Put64(Memory.ScanBase + 8, Memory.Module + 0x13F3070);
        m.Put64(Memory.ScanBase + 16, secondTable);
        var error = Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Contains("ambiguous", error.Message);
        Assert.Equal(2, m.Enumerations);
    }

    [Fact]
    public void ChangedExpectedDeclarationsClearPreviousCacheIdentity()
    {
        var m = new Memory(); Assert.Equal(Memory.Qr, m.Read().QrNode);
        const ulong replacementQr = 0xB10000;
        m.Allocate(replacementQr, 64);
        m.Put(replacementQr, m.Bytes(Memory.Qr, 64));
        m.Put64(Memory.Table + 24, replacementQr);
        m.Put64(Memory.Extra + 24, replacementQr + 24);
        m.Put32(Memory.Extra + 48, 7);
        m.Expected["extra"] = 7;
        // New source metadata must not inherit either old table headers or old QR identity.
        var result = m.Read();
        Assert.Equal(replacementQr, result.QrNode);
        Assert.NotNull(result.UnitPointer);
        Assert.Equal(2, m.Enumerations);
        // Full declaration checks still apply after the fingerprint change.
        m.Expected["extra"] = 4;
        Assert.Throws<InvalidDataException>(() => m.Read());
        Assert.Equal(3, m.Enumerations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminatedQrNameAtPageEndDoesNotReadUnmappedNextPage(bool zeroSlot)
    {
        var m = new Memory();
        const ulong page = 0xB00000, name = 0xB00FF8;
        m.Allocate(page, 0x1000); // The following page is deliberately unmapped.
        m.Put(name, Encoding.ASCII.GetBytes("QR\0"));
        m.Put64(Memory.Qr + 40, name);
        if (zeroSlot) m.Put32(Memory.ArrayData, 0);
        var result = m.Read();
        Assert.Equal(zeroSlot, result.UnitPointer is null);
        Assert.Equal(2, m.Calls.Count(c => c == (name, 8)));
        Assert.DoesNotContain(m.Calls, c => c.Address >= page && c.Address < page + 0x1000 &&
            c.Address + (ulong)c.Length > page + 0x1000);
        Assert.DoesNotContain(m.Calls, c => c.Address == page + 0x1000);
    }

    private const ulong LargeScanBase = 0x1000000;
    private static Memory SizedDiscovery(int blockBytes)
    {
        var m = new Memory();
        m.Allocate(LargeScanBase, blockBytes + 64);
        // Unaligned region start: the aligned owner QWORD has only its FIRST byte in the first block.
        var owner = LargeScanBase + (ulong)blockBytes;
        m.Put64(owner, Memory.Instance); m.Put64(owner + 8, Memory.Module + 0x13F3070); m.Put64(owner + 16, Memory.Table);
        m.RegionOverride = () => new[] { new MemoryRegion(LargeScanBase + 1, (ulong)blockBytes + 63) };
        return m;
    }

    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(4 * 1024 * 1024)]
    public void DiscoveryBlockBoundaryUsesSevenByteOverlapAndNeverCrossesRegion(int blockBytes)
    {
        var m = SizedDiscovery(blockBytes);
        var result = m.Read(discoveryBlockBytes: blockBytes);
        var owner = LargeScanBase + (ulong)blockBytes;
        Assert.Equal(owner, result.OwnerAggregate); Assert.Equal(Memory.Unit, result.UnitPointer);
        Assert.Contains((LargeScanBase + 1, blockBytes), m.Calls);
        Assert.Contains((owner - 6, 70), m.Calls); // First block ends at owner+1; next begins seven bytes earlier.
        Assert.Contains((owner, 24), m.Calls);
        var regionEnd = owner + 64;
        var withinScan = m.Calls.Where(c => c.Address >= LargeScanBase + 1 && c.Address < regionEnd).ToArray();
        Assert.All(withinScan, c => { Assert.InRange(c.Length, 1, blockBytes); Assert.True(c.Address + (ulong)c.Length <= regionEnd); });
        Assert.Equal((ulong)blockBytes + 70, withinScan.Where(c => c.Length > 64).Aggregate(0UL, (n, c) => n + (ulong)c.Length));
    }

    [Fact]
    public void OmittedDiscoverySizeRetainsTheOriginal64KiBReadPattern()
    {
        var m = new Memory();
        var result = m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World, m.Session, m.Expected);
        Assert.Equal(Memory.Aggregate, result.OwnerAggregate);
        Assert.Contains((Memory.ScanBase, 65536), m.Calls);
        Assert.Contains((Memory.ScanBase + 65536 - 7, 55), m.Calls);
        Assert.DoesNotContain(m.Calls, c => c.Length > 65536);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FourMiBDiscoveryStillVisitsLastRegionAndRejectsSecondOwnerAfterCachedSuccess(bool zeroSlot)
    {
        const int block = 4 * 1024 * 1024;
        const ulong lastRegion = 0x2000000;
        var m = SizedDiscovery(block); Assert.NotNull(m.Read(discoveryBlockBytes: block).UnitPointer);
        if (zeroSlot) m.Put32(Memory.ArrayData, 0);
        m.Allocate(lastRegion, 64);
        m.Put64(lastRegion, Memory.Instance); m.Put64(lastRegion + 8, Memory.Module + 0x13F3070); m.Put64(lastRegion + 16, Memory.Table);
        m.RegionOverride = () => new[] { new MemoryRegion(LargeScanBase + 1, block + 63UL), new MemoryRegion(lastRegion, 64) };
        m.Calls.Clear();
        var error = Assert.Throws<InvalidDataException>(() => m.Read(discoveryBlockBytes: block));
        Assert.Contains("ambiguous", error.Message); Assert.Equal(2, m.Enumerations);
        Assert.Contains((LargeScanBase + 1, block), m.Calls); Assert.Contains((lastRegion, 64), m.Calls);
    }

    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(4 * 1024 * 1024)]
    public void DiscoveryBudgetIncludesOverlapAndRejectsNextRegionBeforeItsReads(int blockBytes)
    {
        const ulong cap = 8UL * 1024 * 1024 * 1024, lastRegion = 0x2000000;
        var m = SizedDiscovery(blockBytes); var firstSize = (ulong)blockBytes + 63;
        // Declared region sizes total exactly the cap, but the first region's seven repeated bytes already count.
        m.RegionOverride = () => new[] { new MemoryRegion(LargeScanBase + 1, firstSize), new MemoryRegion(lastRegion, cap - firstSize) };
        var error = Assert.Throws<InvalidDataException>(() => m.Read(discoveryBlockBytes: blockBytes));
        Assert.Equal("Incomplete discovery: region exceeds remaining byte cap.", error.Message);
        Assert.DoesNotContain(m.Calls, c => c.Address == lastRegion);
    }

    [Fact]
    public void FourMiBShortBulkReadIsRejectedAndCannotPopulateCache()
    {
        const int block = 4 * 1024 * 1024;
        var m = SizedDiscovery(block); m.ShortAt = LargeScanBase + 1;
        var error = Assert.Throws<InvalidDataException>(() => m.Read(discoveryBlockBytes: block));
        Assert.Contains("short read", error.Message);
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.Qr);
        m.ShortAt = null; Assert.NotNull(m.Read(discoveryBlockBytes: block).UnitPointer);
        Assert.Equal(2, m.Enumerations);
    }

    [Fact]
    public void FourMiBCancellationAfterBulkReturnPropagatesAndPreCancelledDoesNoIo()
    {
        const int block = 4 * 1024 * 1024;
        var m = SizedDiscovery(block);
        using var first = new CancellationTokenSource(); first.Cancel();
        Assert.Throws<OperationCanceledException>(() => m.Read(first.Token, block)); Assert.Empty(m.Calls);
        using var during = new CancellationTokenSource();
        m.BeforeRead = (a, n) => { if (a == LargeScanBase + 1 && n == block) during.Cancel(); };
        Assert.Throws<OperationCanceledException>(() => m.Read(during.Token, block));
        Assert.Contains((LargeScanBase + 1, block), m.Calls);
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.Qr);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(int.MinValue)] [InlineData(65535)] [InlineData(65537)]
    [InlineData(128 * 1024)] [InlineData(4 * 1024 * 1024 - 1)] [InlineData(4 * 1024 * 1024 + 1)] [InlineData(int.MaxValue)]
    public void UnsupportedDiscoverySizesRejectBeforeAnyTargetReadOrRegionEnumeration(int bytes)
    {
        var m = new Memory();
        var error = Assert.Throws<InvalidDataException>(() => m.Read(discoveryBlockBytes: bytes));
        Assert.Equal("Unsupported diagnostic discovery block size.", error.Message);
        Assert.Empty(m.Calls); Assert.Equal(0, m.Enumerations);
    }

    [Fact]
    public void StandaloneFourMiBSelectionLeavesMetadataReadSequenceUnchanged()
    {
        Assert.Equal(4 * 1024 * 1024, StandaloneScalarRunner.DiscoveryBlockBytes);
        var baseline = new Memory(); baseline.Read();
        var large = new Memory(); large.Read(discoveryBlockBytes: StandaloneScalarRunner.DiscoveryBlockBytes);
        // Only the default fixture's two bulk blocks are replaced by one region-bounded block.
        bool Bulk((ulong Address, int Length) c) => c.Address == Memory.ScanBase || c.Address == Memory.ScanBase + 65536 - 7;
        Assert.Equal(baseline.Calls.Where(c => !Bulk(c)).ToArray(), large.Calls.Where(c => !Bulk(c)).ToArray());
        Assert.Contains((Memory.ScanBase, 0x10030), large.Calls);
        var boundary = SizedDiscovery(StandaloneScalarRunner.DiscoveryBlockBytes);
        Assert.NotNull(boundary.Read(discoveryBlockBytes: StandaloneScalarRunner.DiscoveryBlockBytes).UnitPointer);
        Assert.Contains((LargeScanBase + 1, 4 * 1024 * 1024), boundary.Calls);
    }

    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(4 * 1024 * 1024)]
    public void DiscoverySizingCannotBypassTypedMetadataRejection(int blockBytes)
    {
        var m = SizedDiscovery(blockBytes); m.Put32(Memory.Qr + 52, 7);
        var error = Assert.Throws<InvalidDataException>(() => m.Read(discoveryBlockBytes: blockBytes));
        Assert.Equal("Source globals type mismatch.", error.Message);
    }

    [Fact]
    public void SourceScopeObserverPublishesValidatedHashOnlyMetadataWithNoAdditionalTargetReads()
    {
        var baseline = new Memory(); baseline.Read();
        var m = new Memory(); Warcraft300SourceScopeAudit? audit = null; int callbacks = 0, callsAtPublish = -1;
        var result = m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World, m.Session, m.Expected,
            sourceScopeObserver: snapshot => { callbacks++; audit = snapshot; callsAtPublish = m.Calls.Count; });
        Assert.NotNull(result.UnitPointer); Assert.Equal(1, callbacks); Assert.NotNull(audit);
        Assert.Equal(baseline.Calls.ToArray(), m.Calls.ToArray()); Assert.Equal(m.Calls.Count, callsAtPublish);
        Assert.Equal(2, audit!.TotalCount); Assert.Equal(1, audit.ExpectedSourceDeclarationCount);
        Assert.Equal(1, audit.MatchedExpectedMapDeclarationCount); Assert.Equal(1, audit.OtherIdentifierCount);
        var expected = Assert.Single(audit.Entries, e => e.IsExpectedMapDeclaration);
        Assert.Equal(Warcraft300SourceScopeAudit.HashIdentifier("QR"), expected.NormalizedNameSha256);
        Assert.Equal(12U, expected.RuntimeTypeTag); Assert.Equal(12U, expected.DeclaredTypeTag);
        var other = Assert.Single(audit.Entries, e => !e.IsExpectedMapDeclaration);
        Assert.Equal(Warcraft300SourceScopeAudit.HashIdentifier("extra"), other.NormalizedNameSha256);
        Assert.Equal(5, other.IdentifierLength); Assert.Equal(0U, other.RuntimeTypeTag); Assert.Equal(7U, other.DeclaredTypeTag);
    }

    [Theory]
    [InlineData("source-type")]
    [InlineData("name-recheck")]
    [InlineData("qr-recheck")]
    [InlineData("final-context")]
    [InlineData("owner-ambiguity")]
    public void SourceScopeObserverIsNeverInvokedOnAnyExistingGuardFailure(string failure)
    {
        var m = new Memory(); int callbacks = 0, instanceReads = 0;
        if (failure == "source-type") m.Put32(Memory.Qr + 52, 4);
        if (failure == "owner-ambiguity")
        {
            m.Put64(Memory.ScanBase, Memory.Instance); m.Put64(Memory.ScanBase + 8, Memory.Module + 0x13F3070);
            m.Put64(Memory.ScanBase + 16, Memory.Table);
        }
        m.BeforeRead = (a, n) =>
        {
            if (a == Memory.Qr + 24 && failure == "name-recheck") m.Name(Memory.QrName, "QS");
            if (a == Memory.Qr + 24 && failure == "qr-recheck") m.Put32(Memory.ArrayData, 0);
            if (a == Memory.Instance && n == 48 && ++instanceReads == 2 && failure == "final-context") m.Put32(Memory.Instance + 8, 2);
        };
        Assert.Throws<InvalidDataException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World,
            m.Session, m.Expected, sourceScopeObserver: _ => callbacks++));
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public void SourceScopeObserverIsNeverInvokedForPreCancelledRead()
    {
        var m = new Memory(); int callbacks = 0;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World,
            m.Session, m.Expected, cts.Token, sourceScopeObserver: _ => callbacks++));
        Assert.Equal(0, callbacks); Assert.Empty(m.Calls);
    }

    [Fact]
    public void DeclaredScopeSnapshotIsSameInvocationOnlyAndEveryReadStillDiscoversUniqueness()
    {
        var m=new Memory(); var scope=new Warcraft300DeclaredScope(m.Expected,
            new[]{new KeyValuePair<string,Warcraft300Declaration>("extra",new(7,false,false))},"synthetic-policy");
        Warcraft300GrowthObservation Read()=>m.Reader.Read(m.Bytes,m.Regions,Memory.Module,m.View,Memory.World,m.Session,m.Expected,declaredScope:scope);
        var first=Read(); Assert.Same(first.GlobalsSnapshot,m.Reader.CompletedSnapshot);
        Assert.Equal(0U,first.GlobalsSnapshot!.Nodes.Single(n=>n.Name=="extra").RuntimeTag);
        var second=Read(); Assert.NotSame(first.GlobalsSnapshot,second.GlobalsSnapshot);
        Assert.NotEqual(first.GlobalsSnapshot.Invocation,second.GlobalsSnapshot!.Invocation); Assert.Equal(2,m.Enumerations);
        m.Put64(Memory.ScanBase,Memory.Instance); m.Put64(Memory.ScanBase+8,Memory.Module+0x13F3070); m.Put64(Memory.ScanBase+16,Memory.Table);
        Assert.Throws<InvalidDataException>(()=>Read()); Assert.Null(m.Reader.CompletedSnapshot); Assert.Equal(3,m.Enumerations);
    }
    [Fact]
    public void DeclaredScopeRejectsUnknownMissingAndStrictRuntimeMetadata()
    {
        var m=new Memory();
        var missing=new Warcraft300DeclaredScope(m.Expected,Array.Empty<KeyValuePair<string,Warcraft300Declaration>>(),"synthetic-policy");
        Assert.Throws<InvalidDataException>(()=>m.Reader.Read(m.Bytes,m.Regions,Memory.Module,m.View,Memory.World,m.Session,m.Expected,declaredScope:missing));
        var strict=new Warcraft300DeclaredScope(m.Expected,new[]{new KeyValuePair<string,Warcraft300Declaration>("extra",new(7,false,true))},"strict");
        Assert.Throws<InvalidDataException>(()=>m.Reader.Read(m.Bytes,m.Regions,Memory.Module,m.View,Memory.World,m.Session,m.Expected,declaredScope:strict));
        Assert.Null(m.Reader.CompletedSnapshot);
    }

    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(4 * 1024 * 1024)]
    public void ReusedDiscoveryBufferPreservesExactReadSequenceAndIgnoresUnusedTail(int blockBytes)
    {
        var baseline = SizedDiscovery(blockBytes); var expected = baseline.Read(discoveryBlockBytes: blockBytes);
        var m = SizedDiscovery(blockBytes); var buffers = new List<byte[]>();
        int Fill(ulong address, byte[] buffer, int count)
        {
            buffers.Add(buffer); Assert.Equal(blockBytes, buffer.Length);
            var bytes = m.Bytes(address, count); bytes.CopyTo(buffer, 0);
            if (count + 24 <= buffer.Length)
            {
                BitConverter.GetBytes(Memory.Instance).CopyTo(buffer, count);
                BitConverter.GetBytes(Memory.Module + 0x13F3070).CopyTo(buffer, count + 8);
                BitConverter.GetBytes(Memory.Table).CopyTo(buffer, count + 16);
            }
            return bytes.Length;
        }
        var actual = m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World,
            m.Session, m.Expected, discoveryBlockBytes: blockBytes, readDiscoveryBlock: Fill);
        Assert.Equal(2, buffers.Count); Assert.Same(buffers[0], buffers[1]);
        Assert.Equal(baseline.Calls.ToArray(), m.Calls.ToArray());
        Assert.Equal(expected.UnitPointer, actual.UnitPointer); Assert.Equal(expected.OwnerAggregate, actual.OwnerAggregate);
        Assert.Equal(expected.Allocation, actual.Allocation);
        m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World,
            m.Session, m.Expected, discoveryBlockBytes: blockBytes, readDiscoveryBlock: Fill);
        Assert.Equal(4, buffers.Count); Assert.NotSame(buffers[0], buffers[2]); Assert.Same(buffers[2], buffers[3]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65535)]
    [InlineData(65537)]
    [InlineData(-1)]
    public void ReusedDiscoveryBufferRejectsIncompleteOrInvalidCountBeforeReadingStaleBytes(int returned)
    {
        var m = new Memory(); int fills = 0;
        var error = Assert.Throws<InvalidDataException>(() => m.Reader.Read(m.Bytes, m.Regions,
            Memory.Module, m.View, Memory.World, m.Session, m.Expected,
            readDiscoveryBlock: (address, buffer, count) => { fills++; return returned; }));
        Assert.Contains("short read", error.Message); Assert.Equal(1, fills);
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.Qr);
        Assert.NotNull(m.Read().UnitPointer); Assert.Equal(2, m.Enumerations);
    }

    [Fact]
    public void ReusedDiscoveryBufferStillChecksLastRegionForAnotherOwnerAfterCachedSuccess()
    {
        const int block = 64 * 1024; const ulong lastRegion = 0x2000000;
        var m = SizedDiscovery(block);
        int Fill(ulong address, byte[] buffer, int count)
        { var bytes = m.Bytes(address, count); bytes.CopyTo(buffer, 0); return bytes.Length; }
        Warcraft300GrowthObservation Read() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View,
            Memory.World, m.Session, m.Expected, readDiscoveryBlock: Fill);
        Assert.NotNull(Read().UnitPointer); m.Put32(Memory.ArrayData, 0);
        m.Allocate(lastRegion, 64); m.Put64(lastRegion, Memory.Instance);
        m.Put64(lastRegion + 8, Memory.Module + 0x13F3070); m.Put64(lastRegion + 16, Memory.Table);
        m.RegionOverride = () => new[] { new MemoryRegion(LargeScanBase + 1, block + 63UL), new MemoryRegion(lastRegion, 64) };
        var error = Assert.Throws<InvalidDataException>(() => Read());
        Assert.Contains("ambiguous", error.Message); Assert.Contains((lastRegion, 64), m.Calls);
        Assert.Equal(2, m.Enumerations);
    }

    [Fact]
    public void ReusedDiscoveryBufferHonorsCancellationBeforeAndAfterRead()
    {
        var m = new Memory(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); int fills = 0;
        Assert.Throws<OperationCanceledException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module,
            m.View, Memory.World, m.Session, m.Expected, cancelled.Token,
            readDiscoveryBlock: (a, buffer, n) => { fills++; return n; }));
        Assert.Empty(m.Calls); Assert.Equal(0, fills);
        using var during = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module,
            m.View, Memory.World, m.Session, m.Expected, during.Token,
            readDiscoveryBlock: (a, buffer, n) => { fills++; during.Cancel(); return n; }));
        Assert.Equal(1, fills); Assert.DoesNotContain(m.Calls, c => c.Address == Memory.Qr);
    }

    [Fact]
    public void DiscoveryCheckpointsAreSerializedAtBoundariesBeforeQrPayload()
    {
        var m = new Memory();
        var checkpoints = new List<int>();
        var result = m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View, Memory.World,
            m.Session, m.Expected, discoveryCheckpoint: () =>
            {
                Assert.DoesNotContain(m.Calls, c => c.Address == Memory.ArrayData);
                checkpoints.Add(m.Calls.Count);
            });
        Assert.True(checkpoints.Count >= 6);
        Assert.True(checkpoints.Zip(checkpoints.Skip(1), (a, b) => a <= b).All(x => x));
        Assert.Equal(1, m.Enumerations);
        Assert.NotNull(result.Allocation);
    }

    [Fact]
    public void CheckpointFailureClearsGrowthAndCancellationStopsBeforeQr()
    {
        var m = new Memory(); m.Read();
        var error = Assert.Throws<InvalidOperationException>(() => m.Reader.Read(m.Bytes, m.Regions,
            Memory.Module, m.View, Memory.World, m.Session, m.Expected,
            discoveryCheckpoint: () => throw new InvalidOperationException("checkpoint failed")));
        Assert.Equal("checkpoint failed", error.Message);
        Assert.Null(m.Reader.CompletedSnapshot);
        m.Calls.Clear(); using var source = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => m.Reader.Read(m.Bytes, m.Regions,
            Memory.Module, m.View, Memory.World, m.Session, m.Expected, source.Token,
            discoveryCheckpoint: source.Cancel));
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.ArrayData);
        Assert.Null(m.Reader.CompletedSnapshot);
        Assert.NotNull(m.Read().UnitPointer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitKnownContextReuseNeverEnumeratesOrBulkReadsAndReadsTheCurrentQr(bool initiallyAbsent)
    {
        var m = new Memory();
        if (initiallyAbsent) m.Put32(Memory.ArrayData, 0);
        var first = m.ReadKnown();
        Assert.Equal(Warcraft300OwnerDiscoveryMode.FullDiscovery, first.OwnerDiscoveryMode);
        Assert.True(first.OwnerSetUniquenessChecked);
        m.Put32(Memory.ArrayData, initiallyAbsent ? 0x100000U : 0U);
        m.Calls.Clear();
        var second = m.Reader.Read(m.Bytes,
            () => throw new InvalidOperationException("Cached reads must not enumerate regions"),
            Memory.Module, m.View, Memory.World, m.Session, m.Expected,
            readDiscoveryBlock: (_, _, _) => throw new InvalidOperationException("Cached reads must not read bulk blocks"),
            discoveryCheckpoint: () => throw new InvalidOperationException("No discovery checkpoint on cache hit"),
            ownerDiscoveryMode: Warcraft300OwnerDiscoveryMode.RevalidateKnownContext);
        Assert.Equal(initiallyAbsent ? Memory.Unit : (ulong?)null, second.UnitPointer);
        Assert.Equal(Warcraft300OwnerDiscoveryMode.RevalidateKnownContext, second.OwnerDiscoveryMode);
        Assert.False(second.OwnerSetUniquenessChecked);
        Assert.Contains("current owner-set uniqueness not rechecked", second.StatusDescription);
        Assert.Equal(1, m.Enumerations);
        Assert.Contains((Memory.ArrayData, 4), m.Calls);
        Assert.DoesNotContain(m.Calls, c => c.Address == Memory.ScanBase);
        Assert.Null(second.GlobalsSnapshot);
    }

    [Fact]
    public void KnownContextReuseReadsNewRawcodeAndNativeAllocationInsteadOfCachingGrowthValues()
    {
        var m = new Memory(); m.ReadKnown();
        m.Put32(Memory.Unit + 0x178, 0x48303032);
        m.Put32(Memory.Unit + 0x1C, 6); m.Put32(Memory.Record + 0x24, 6);
        var changed = m.ReadKnown();
        Assert.Equal(0x48303032U, changed.Rawcode);
        Assert.Equal(1, m.Enumerations);
        Assert.NotNull(changed.Allocation);
        m.Put32(Memory.ArrayData, 0);
        Assert.Null(m.ReadKnown().Allocation);
        m.Put32(Memory.ArrayData, 0x100000);
        Assert.Equal(0x48303032U, m.ReadKnown().Rawcode);
        Assert.Equal(1, m.Enumerations);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("view")]
    [InlineData("root")]
    [InlineData("world")]
    [InlineData("ui")]
    [InlineData("instance")]
    [InlineData("script")]
    [InlineData("manager")]
    [InlineData("registry")]
    [InlineData("instance-header")]
    [InlineData("script-header")]
    [InlineData("declarations")]
    public void KnownContextReuseRediscoveriesOnEveryIdentityComponentChangeEvenInTheSameSession(string change)
    {
        var m = new Memory(); m.ReadKnown();
        const ulong replacement = 0xB10000;
        void Copy(ulong address) { m.Allocate(replacement, 0x3000); m.Put(replacement, m.Bytes(address, 0x3000)); }
        switch (change)
        {
            case "session": m.Session = "next-session"; break;
            case "view": m.SetView(1); m.Put32(Memory.Array + 8, 2); m.Put32(Memory.ArrayData + 4, 0x100000); break;
            case "root":
                Copy(Memory.Root);
                ulong encoded;
                unchecked { encoded = BitOperations.RotateRight(((replacement - 0x2D2C27903E7F5D3DUL) ^ 0x3A11C7B7EF67132BUL) - 0x5BE06F37FC9B5B29UL, 29); }
                m.Put64(Memory.Module + 0x2E9AD00, encoded); m.View = new(replacement, 0, Memory.Player); break;
            case "world": Copy(Memory.World); m.CurrentWorld = replacement; break;
            case "ui": Copy(Memory.Ui); m.Put64(Memory.Module + 0x2F5EF00, replacement); m.Put64(Memory.Module + 0x2F85360, replacement); m.Put64(Memory.World + 0x40, replacement); break;
            case "instance": Copy(Memory.Instance); m.Put64(Memory.Root + 0x25D0, replacement); m.Put64(Memory.Aggregate, replacement); break;
            case "script": Copy(Memory.Script); m.Put64(Memory.Root + 0x25E0, replacement); break;
            case "manager": Copy(Memory.Manager); m.Put64(Memory.Root + 0x2620, replacement); break;
            case "registry": Copy(Memory.Registry); m.Put64(Memory.Module + 0x2F807F0, replacement); break;
            case "instance-header": m.Put32(Memory.Instance + 8, 2); break;
            case "script-header": m.Put32(Memory.Script + 8, 2); break;
            case "declarations": m.Put32(Memory.Extra + 48, 7); m.Expected["extra"] = 7; break;
        }
        var next = m.ReadKnown();
        Assert.NotNull(next.UnitPointer);
        Assert.Equal(2, m.Enumerations);
        Assert.Equal(Warcraft300OwnerDiscoveryMode.FullDiscovery, next.OwnerDiscoveryMode);
        Assert.True(next.OwnerSetUniquenessChecked);
        Assert.False(m.ReadKnown().OwnerSetUniquenessChecked);
        Assert.Equal(2, m.Enumerations);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("table")]
    [InlineData("qr")]
    [InlineData("name")]
    public void KnownContextStructuralChangesRejectTheCachedReadAndNextAttemptDiscoversAgain(string change)
    {
        var m = new Memory(); m.ReadKnown();
        switch (change)
        {
            case "owner": m.Put64(Memory.Aggregate + 8, Memory.Module); break;
            case "table": m.Put32(Memory.Table + 64, 1); break;
            case "qr":
                const ulong replacementQr = 0xB10000;
                m.Allocate(replacementQr, 64); m.Put(replacementQr, m.Bytes(Memory.Qr, 64));
                m.Put64(Memory.Table + 24, replacementQr); m.Put64(Memory.Extra + 24, replacementQr + 24); break;
            case "name": m.Name(Memory.QrName, "QS"); break;
        }
        Assert.Throws<InvalidDataException>(() => m.ReadKnown());
        Assert.Equal(1, m.Enumerations);
        Assert.Null(m.Reader.CompletedSnapshot);
        if (change == "owner") m.Put64(Memory.Aggregate + 8, Memory.Module + 0x13F3070);
        if (change == "name") m.Name(Memory.QrName, "QR");
        Assert.True(m.ReadKnown().OwnerSetUniquenessChecked);
        Assert.Equal(2, m.Enumerations);
    }

    [Theory]
    [InlineData("slot")]
    [InlineData("header")]
    [InlineData("context")]
    public void KnownContextReuseStillRejectsMidSampleChanges(string change)
    {
        var m = new Memory(); m.ReadKnown();
        m.BeforeRead = (address, _) =>
        {
            if (address != Memory.Qr + 24) return;
            m.BeforeRead = null;
            if (change == "slot") m.Put32(Memory.ArrayData, 0);
            if (change == "header") m.Put32(Memory.Table + 64, 1);
            if (change == "context") m.Put32(Memory.Script + 8, 2);
        };
        Assert.Throws<InvalidDataException>(() => m.ReadKnown());
        Assert.Null(m.BeforeRead);
        Assert.Equal(1, m.Enumerations);
        Assert.True(m.ReadKnown().OwnerSetUniquenessChecked);
        Assert.Equal(2, m.Enumerations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedReferenceDoesNotClaimToDiscoverNewOwnersButStrictDefaultStillRejectsThem(bool zeroSlot)
    {
        var m = new Memory(); m.ReadKnown();
        if (zeroSlot) m.Put32(Memory.ArrayData, 0);
        m.Put64(Memory.ScanBase, Memory.Instance); m.Put64(Memory.ScanBase + 8, Memory.Module + 0x13F3070);
        m.Put64(Memory.ScanBase + 16, Memory.Table);
        var reference = m.ReadKnown();
        Assert.False(reference.OwnerSetUniquenessChecked);
        Assert.Contains("uniqueness not rechecked", reference.StatusDescription);
        Assert.Equal(1, m.Enumerations);
        Assert.Contains("ambiguous", Assert.Throws<InvalidDataException>(() => m.Read()).Message);
        Assert.Equal(2, m.Enumerations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopeAuditsForceFullDiscoveryEvenWhenKnownContextReuseWasRequested(bool declaredScope)
    {
        var m = new Memory(); m.ReadKnown();
        var scope = new Warcraft300DeclaredScope(m.Expected,
            new[] { new KeyValuePair<string, Warcraft300Declaration>("extra", new(7, false, false)) }, "synthetic-policy");
        var callbacks = 0;
        Warcraft300GrowthObservation Read() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module, m.View,
            Memory.World, m.Session, m.Expected,
            sourceScopeObserver: declaredScope ? null : _ => callbacks++, declaredScope: declaredScope ? scope : null,
            ownerDiscoveryMode: Warcraft300OwnerDiscoveryMode.RevalidateKnownContext);
        var result = Read();
        Assert.Equal(Warcraft300OwnerDiscoveryMode.FullDiscovery, result.OwnerDiscoveryMode);
        Assert.True(result.OwnerSetUniquenessChecked); Assert.Equal(2, m.Enumerations);
        Assert.Equal(declaredScope ? 0 : 1, callbacks);
        m.Put64(Memory.ScanBase, Memory.Instance); m.Put64(Memory.ScanBase + 8, Memory.Module + 0x13F3070);
        m.Put64(Memory.ScanBase + 16, Memory.Table);
        Assert.Throws<InvalidDataException>(() => Read());
        Assert.Equal(3, m.Enumerations); Assert.Equal(declaredScope ? 0 : 1, callbacks);
    }

    [Fact]
    public void KnownContextResetAndCancellationCannotKeepOldCachedAuthority()
    {
        var m = new Memory(); m.ReadKnown(); m.Reader.Reset();
        Assert.True(m.ReadKnown().OwnerSetUniquenessChecked); Assert.Equal(2, m.Enumerations);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); m.Calls.Clear();
        Assert.Throws<OperationCanceledException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module,
            m.View, Memory.World, m.Session, m.Expected, cancel.Token,
            ownerDiscoveryMode: Warcraft300OwnerDiscoveryMode.RevalidateKnownContext));
        Assert.Empty(m.Calls);
        Assert.True(m.ReadKnown().OwnerSetUniquenessChecked); Assert.Equal(3, m.Enumerations);
    }

    [Fact]
    public void UnknownOwnerDiscoveryModeIsRejectedBeforeAnyTargetRead()
    {
        var m = new Memory();
        Assert.Throws<InvalidDataException>(() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module,
            m.View, Memory.World, m.Session, m.Expected, ownerDiscoveryMode: (Warcraft300OwnerDiscoveryMode)99));
        Assert.Empty(m.Calls); Assert.Equal(0, m.Enumerations);
    }

    [Fact]
    public void AddingPinnedSourceIdentityRediscoveriesEvenWhenDeclarationsAndSessionAreUnchanged()
    {
        var m = new Memory();
        var source = Map2320GrowthSource.LoadBundled("2.320");
        var additional = source.Globals.Where(p => p.Key != "QR").ToArray();
        const ulong start = 0xB00000, stride = 320;
        m.Allocate(start, checked(additional.Length * (int)stride));
        var previous = Memory.Extra + 24;
        m.Put64(Memory.Extra + 32, start);
        for (var i = 0; i < additional.Length; i++)
        {
            var address = start + (ulong)i * stride;
            var next = i + 1 == additional.Length ? Memory.Table + 17 : address + stride;
            m.Put64(address + 24, previous); m.Put64(address + 32, next); m.Put64(address + 40, address + 64);
            m.Put32(address + 48, (uint)additional[i].Value); m.Put32(address + 52, (uint)additional[i].Value);
            m.Put(address + 64, Encoding.ASCII.GetBytes(additional[i].Key + "\0"));
            m.Expected[additional[i].Key] = additional[i].Value;
            previous = address + 24;
        }
        m.Put64(Memory.Table + 16, previous);
        Assert.True(m.ReadKnown().OwnerSetUniquenessChecked);
        Warcraft300GrowthObservation ReadPinned() => m.Reader.Read(m.Bytes, m.Regions, Memory.Module,
            m.View, Memory.World, m.Session, m.Expected, source: source,
            ownerDiscoveryMode: Warcraft300OwnerDiscoveryMode.RevalidateKnownContext);
        var pinned = ReadPinned();
        Assert.True(pinned.OwnerSetUniquenessChecked); Assert.Equal(2, m.Enumerations);
        Assert.NotNull(pinned.RoundInputs);
        Assert.False(ReadPinned().OwnerSetUniquenessChecked); Assert.Equal(2, m.Enumerations);
        Assert.True(m.ReadKnown().OwnerSetUniquenessChecked); Assert.Equal(3, m.Enumerations);
    }

    private sealed class Memory
    {
        internal const ulong Module = 0x140000000, Root = 0x200000, Player = 0x300000,
            World = 0x310000, Ui = 0x320000, Instance = 0x400000, Script = 0x410000,
            Manager = 0x420000, Registry = 0x430000, ScanBase = 0x600000, Aggregate = ScanBase + 0xFFF8,
            Table = 0x700000, Qr = 0x710000, Extra = 0x720000, QrName = 0x730000, ExtraName = 0x740000,
            Array = 0x750000, ArrayData = 0x760000, JassTable = 0x800000, Unit = 0x900000,
            NativeTable = 0xA00000, Record = 0xA10000;
        private readonly List<(ulong Address, byte[] Bytes)> blocks = new();
        internal readonly Warcraft300GrowthReader Reader = new();
        internal readonly Dictionary<string, int> Expected = new(StringComparer.Ordinal) { ["QR"] = 12 };
        internal readonly List<(ulong Address, int Length)> Calls = new();
        internal Warcraft300Diagnostic.View View = new(Root, 0, Player);
        internal string Session = "session";
        internal ulong CurrentWorld = World;
        internal int Enumerations;
        internal Action<ulong, int>? BeforeRead;
        internal ulong? ShortAt;
        internal Func<IEnumerable<MemoryRegion>>? RegionOverride;
        internal Memory()
        {
            foreach (var a in new[] { Root, Player, World, Ui, Instance, Script, Manager, Registry,
                Table, Qr, Extra, QrName, ExtraName, Array, ArrayData, JassTable, Unit, NativeTable, Record }) Allocate(a, 0x3000);
            Allocate(ScanBase, 0x10030);
            foreach (var rva in new ulong[] { 0x2E9AD00, 0x2F5EF00, 0x2F85360, 0x2F807F0 }) Allocate(Module + rva, 8);
            ulong encoded;
            unchecked { encoded = BitOperations.RotateRight(((Root - 0x2D2C27903E7F5D3DUL) ^ 0x3A11C7B7EF67132BUL) - 0x5BE06F37FC9B5B29UL, 29); }
            Put64(Module + 0x2E9AD00, encoded);
            Put64(Root, Module + 0x26C8C70); Put32(Root + 0x2698, 28); SetView(0);
            Put64(Player, Module + 0x26C87D8); Put64(World, Module + 0x2764A20); Put64(World + 0x40, Ui);
            Put64(Ui, Module + 0x275ED08); Put64(Module + 0x2F5EF00, Ui); Put64(Module + 0x2F85360, Ui);
            Put64(Root + 0x25D0, Instance); Put64(Root + 0x25E0, Script); Put64(Root + 0x2620, Manager);
            Put64(Instance, Module + 0x27ECBC0); Put64(Script, Module + 0x27ECC40);
            Put64(Module + 0x2F807F0, Registry);
            Put64(Aggregate, Instance); Put64(Aggregate + 8, Module + 0x13F3070); Put64(Aggregate + 16, Table);
            Put64(Table, Module + 0x2809B78); Put32(Table + 8, 24); Put64(Table + 24, Qr); Put64(Table + 16, Extra + 24);
            Put64(Qr + 24, Table + 16); Put64(Qr + 32, Extra); Put64(Qr + 40, QrName);
            Put32(Qr + 48, 12); Put32(Qr + 52, 12); Put64(Qr + 56, Array); Name(QrName, "QR");
            Put64(Extra + 24, Qr + 24); Put64(Extra + 32, Table + 17); Put64(Extra + 40, ExtraName);
            Put32(Extra + 48, 0); Put32(Extra + 52, 7); Name(ExtraName, "extra");
            Put64(Array, Module + 0x2809EF8); Put32(Array + 8, 1); Put64(Array + 16, ArrayData); Put32(Array + 24, 4);
            Put32(ArrayData, 0x100000); Put32(Manager + 0x290, 1); Put64(Manager + 0x298, JassTable);
            Put32(JassTable, 1); Put64(JassTable + 8, Unit);
            Put64(Unit, Module + 0x2792E78); Put32(Unit + 0x18, 0); Put32(Unit + 0x1C, 5);
            Put32(Unit + 0x1C0, 27); Put32(Unit + 0x178, 0x48303031);
            Put32(Registry + 0x30, 1); Put64(Registry + 0x18, NativeTable);
            Put32(NativeTable, 0xFFFFFFFE); Put64(NativeTable + 8, Record);
            Put32(Record + 0x24, 5); Put32(Record + 0x18, 0x2B61676C); Put64(Record + 0x90, Unit);
        }
        internal void SetView(ushort slot)
        {
            Put(Root + 0x262C, BitConverter.GetBytes(slot)); Put64(Root + 0x26A0 + slot * 8UL, Player);
            View = new(Root, slot, Player);
        }
        internal void Allocate(ulong a, int length) => blocks.Add((a, new byte[length]));
        internal void Put(ulong a, byte[] value)
        {
            var block = blocks.Single(b => a >= b.Address && a - b.Address + (ulong)value.Length <= (ulong)b.Bytes.Length);
            value.CopyTo(block.Bytes, (int)(a - block.Address));
        }
        internal void Put64(ulong a, ulong value) => Put(a, BitConverter.GetBytes(value));
        internal void Put32(ulong a, uint value) => Put(a, BitConverter.GetBytes(value));
        internal void Name(ulong a, string value) { Put(a, new byte[512]); Put(a, Encoding.ASCII.GetBytes(value + "\0")); }
        internal byte[] Bytes(ulong a, int n)
        {
            BeforeRead?.Invoke(a, n); Calls.Add((a, n));
            var found = blocks.Where(b => a >= b.Address && a - b.Address + (ulong)n <= (ulong)b.Bytes.Length).ToArray();
            if (found.Length != 1) throw new InvalidDataException("Unmapped synthetic read");
            var bytes = found[0].Bytes.AsSpan((int)(a - found[0].Address), n).ToArray();
            return ShortAt == a ? bytes[..^1] : bytes;
        }
        internal IEnumerable<MemoryRegion> Regions()
        {
            Enumerations++;
            return RegionOverride?.Invoke() ?? new[] { new MemoryRegion(ScanBase, 0x10030) };
        }
        internal Warcraft300GrowthObservation Read(CancellationToken token = default,
            int discoveryBlockBytes = Warcraft300GrowthReader.DefaultDiscoveryBlockBytes) =>
            Reader.Read(Bytes, Regions, Module, View, World, Session, Expected, token, discoveryBlockBytes);
        internal Warcraft300GrowthObservation ReadKnown() =>
            Reader.Read(Bytes, Regions, Module, View, CurrentWorld, Session, Expected,
                ownerDiscoveryMode: Warcraft300OwnerDiscoveryMode.RevalidateKnownContext);
    }
}
