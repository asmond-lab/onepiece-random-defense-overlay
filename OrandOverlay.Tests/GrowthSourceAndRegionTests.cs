using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GrowthSourceAndRegionTests
{
    private static byte[] Data() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", Map2320GrowthSource.FileName));

    [Fact]
    public void PinnedGrowthDeclarationsAreSeparateFromProductionApproval()
    {
        var source = Map2320GrowthSource.LoadBundled();
        Assert.Equal(3936, source.Globals.Count); Assert.Equal(12, source.Globals["QR"]);
        Assert.Equal(9, source.Globals["hR"]); Assert.Equal(13, source.Globals["JR"]);
        var bundle = Map2320DataBundle.LoadBundled();
        Assert.False(bundle.LiveRecognitionSupported); Assert.Equal(7, Map2320DataBundle.ExpectedMembers.Length);
    }

    [Fact]
    public void CrLfPackagingIsNormalizedButContentMutationIsRejected()
    {
        var source = Encoding.UTF8.GetString(Data());
        Assert.Equal(3936, Map2320GrowthSource.Load(Encoding.UTF8.GetBytes(source.Replace("\n", "\r\n"))).Globals.Count);
        Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load(Encoding.UTF8.GetBytes(source.Replace("\"QR\": 12", "\"QR\": 9"))));
        Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load(Encoding.UTF8.GetBytes(source.Replace("2.320", "2.314"))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(131073)]
    public void SchemaReadIsBounded(int size) => Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load(new byte[size]));

    [Fact]
    public void InvalidUtf8IsAnIntegrityFailure() => Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load([0xFF]));

    private static ReadOnlyProcessMemory.ModuleRegionInfo Region(ulong address, ulong size, uint protect = 4,
        uint state = 0x1000, uint type = 0x20000) => new(address, size, state, protect, type);

    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(8)]
    [InlineData(0x20)] [InlineData(0x40)] [InlineData(0x80)]
    public void StrictWalkIncludesOnlyReadablePrivatePages(int protect)
    {
        var calls = new List<ulong>();
        var regions = ReadOnlyPrivateRegionScan.Enumerate(address =>
        {
            calls.Add(address);
            return address == ReadOnlyPrivateRegionScan.Minimum
                ? Region(address, 4096, (uint)protect)
                : Region(address, ReadOnlyPrivateRegionScan.EndExclusive - address, state: 0x10000, type: 0);
        }).ToArray();
        Assert.Single(regions); Assert.Equal(4096UL, regions[0].Size); Assert.Equal(2, calls.Count);
    }

    [Theory]
    [InlineData(1, 0x1000, 0x20000)]
    [InlineData(0x104, 0x1000, 0x20000)]
    [InlineData(0x10, 0x1000, 0x20000)]
    [InlineData(4, 0x2000, 0x20000)]
    [InlineData(4, 0x1000, 0x1000000)]
    [InlineData(4, 0x1000, 0x40000)]
    public void StrictWalkExcludesUnavailableAndNonPrivateRanges(int protect, int state, int type)
    {
        Assert.Empty(ReadOnlyPrivateRegionScan.Enumerate(address => Region(address,
            ReadOnlyPrivateRegionScan.EndExclusive - address, (uint)protect, (uint)state, (uint)type)));
    }

    [Fact]
    public void FailedQueryNeverMasqueradesAsCompleteEnumeration() =>
        Assert.Throws<InvalidDataException>(() => ReadOnlyPrivateRegionScan.Enumerate(_ => null).ToArray());

    [Theory]
    [InlineData("zero")]
    [InlineData("gap")]
    [InlineData("nonprogress")]
    [InlineData("overflow")]
    public void MalformedQueryBoundsAreRejected(string fault)
    {
        Assert.Throws<InvalidDataException>(() => ReadOnlyPrivateRegionScan.Enumerate(address => fault switch
        {
            "zero" => Region(address, 0),
            "gap" => Region(address + 4096, 4096),
            "nonprogress" => Region(0, address),
            _ => Region(address, ulong.MaxValue)
        }).ToArray());
    }

    [Fact]
    public void QueryCanCoverPastUpperBoundaryWithoutReadingIt()
    {
        var regions = ReadOnlyPrivateRegionScan.Enumerate(address => Region(address,
            ReadOnlyPrivateRegionScan.EndExclusive - address + 4096)).ToArray();
        Assert.Single(regions);
        Assert.Equal(ReadOnlyPrivateRegionScan.EndExclusive, regions[0].BaseAddress + regions[0].Size);
    }

    [Fact]
    public void CancellationStopsBeforeAnyQuery()
    {
        using var source = new CancellationTokenSource(); source.Cancel(); var calls = 0;
        Assert.Throws<OperationCanceledException>(() => ReadOnlyPrivateRegionScan.Enumerate(a =>
        { calls++; return Region(a, 4096); }, source.Token).ToArray());
        Assert.Equal(0, calls);
    }
}
