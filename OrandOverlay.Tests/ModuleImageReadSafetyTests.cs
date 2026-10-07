using System.Reflection;
using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ModuleImageReadSafetyTests
{
    private const ulong Base = 0x140000000;
    private const int Page = 0x1000;
    private static ReadOnlyProcessMemory.ModuleRegionInfo Info(ulong address, ulong size,
        uint protect = 0x02, uint state = 0x1000, uint type = 0x1000000) =>
        new(address, size, state, protect, type);

    [Theory]
    [InlineData(0x1000000)]
    [InlineData(0x20000)]
    [InlineData(0x40000)]
    public void AllowsAllThreeCommittedReadableModuleTypes(uint type)
    {
        var ranges = ReadOnlyProcessMemory.EnumerateReadableModuleRegions(Base, Page,
            address => Info(address, Page, type: type)).ToArray();
        Assert.Equal(new MemoryRegion(Base, Page), Assert.Single(ranges));
    }

    [Theory]
    [InlineData(0x01, 0x1000, 0x1000000)]
    [InlineData(0x102, 0x1000, 0x1000000)]
    [InlineData(0x10, 0x1000, 0x1000000)]
    [InlineData(0x00, 0x1000, 0x1000000)]
    [InlineData(0x02, 0x2000, 0x1000000)]
    [InlineData(0x02, 0x10000, 0)]
    [InlineData(0x02, 0x1000, 0)]
    public void ExcludesUnreadableUncommittedAndUnknownTypes(uint protect, uint state, uint type)
    {
        Assert.Empty(ReadOnlyProcessMemory.EnumerateReadableModuleRegions(Base, Page,
            address => Info(address, Page, protect, state, type)));
    }

    [Fact]
    public void QueriesOnlyModuleBoundsAndClipsFirstAndLastRegions()
    {
        var queries = new List<ulong>();
        var ranges = ReadOnlyProcessMemory.EnumerateReadableModuleRegions(Base, 3 * Page, address =>
        {
            queries.Add(address);
            return address == Base ? Info(Base - Page, 2 * Page) : Info(Base + Page, 8 * Page);
        }).ToArray();
        Assert.Equal(new[] { Base, Base + Page }, queries);
        Assert.Equal(new[] { new MemoryRegion(Base, Page), new MemoryRegion(Base + Page, 2 * Page) }, ranges);
    }

    [Fact]
    public void NoAccessTextDoesNotHideLaterRdataOrMoveItsRvas()
    {
        const int size = 0x2800000;
        const int vtable = 0x2792e78;
        const int descriptor = 0x2792100;
        const int locator = 0x2792200;
        var source = new byte[size];
        Encoding.ASCII.GetBytes(".?AVCUnit@@\0").CopyTo(source, descriptor + 0x10);
        BitConverter.GetBytes(1u).CopyTo(source, locator);
        BitConverter.GetBytes((uint)descriptor).CopyTo(source, locator + 0x0C);
        BitConverter.GetBytes((uint)locator).CopyTo(source, locator + 0x14);
        BitConverter.GetBytes(Base + locator).CopyTo(source, vtable - 8);
        var ranges = ReadOnlyProcessMemory.EnumerateReadableModuleRegions(Base, size, address =>
            address == Base ? Info(Base, Page) : address == Base + Page
                ? Info(address, 0x2700000 - Page, protect: 0x01)
                : Info(address, (ulong)size - 0x2700000));
        var calls = new List<(ulong Address, int Count)>();
        var image = StructuralUnitPoolScanner.ReadImage(Base, size, ranges, (address, buffer, count) =>
        {
            calls.Add((address, count));
            Array.Copy(source, (int)(address - Base), buffer, 0, count);
            return count;
        });
        Assert.Equal(size, image.Length);
        Assert.All(calls, call =>
        {
            Assert.InRange(call.Count, 1, 64 * 1024);
            Assert.True(call.Address == Base || call.Address >= Base + 0x2700000);
            Assert.True(call.Address + (ulong)call.Count <= Base + size);
        });
        Assert.Equal(new[] { Base + vtable }, StructuralUnitPoolScanner.FindClassVftables(image, Base, ".?AVCUnit@@"));
        Assert.Equal(0, image[Page]);
    }

    [Fact]
    public void ShortReadCopiesOnlyActualPrefixAndRecoversLaterPages()
    {
        var calls = new List<ulong>();
        var image = StructuralUnitPoolScanner.ReadImage(Base, 4 * Page,
            [new MemoryRegion(Base, 4 * Page)], (address, buffer, count) =>
            {
                calls.Add(address);
                Array.Fill(buffer, (byte)0xEE); // Unreported bytes must not leak into image.
                var offset = address - Base;
                if (offset == 0) { Array.Fill(buffer, (byte)0x11, 0, Page + 13); return Page + 13; }
                if (offset < 2 * Page) return 0;
                Array.Fill(buffer, (byte)0x22, 0, count);
                return count;
            });
        Assert.All(image.Take(Page + 13), value => Assert.Equal(0x11, value));
        Assert.All(image.Skip(Page + 13).Take(Page - 13), value => Assert.Equal(0, value));
        Assert.All(image.Skip(2 * Page), value => Assert.Equal(0x22, value));
        Assert.Contains(Base + 2 * Page, calls);
    }

    [Fact]
    public void ZeroByteLargeReadRetriesPagesAndFindsLaterReadableData()
    {
        var image = StructuralUnitPoolScanner.ReadImage(Base, 4 * Page,
            [new MemoryRegion(Base, 4 * Page)], (address, buffer, count) =>
            {
                if (address < Base + 2 * Page) return 0;
                Array.Fill(buffer, (byte)0x33, 0, count);
                return count;
            });
        Assert.All(image.Take(2 * Page), value => Assert.Equal(0, value));
        Assert.All(image.Skip(2 * Page), value => Assert.Equal(0x33, value));
    }

    [Fact]
    public void ShortSuccessfulReadsRetryWithoutDiscardingTheRemainder()
    {
        var first = true;
        var image = StructuralUnitPoolScanner.ReadImage(Base, 2 * Page,
            [new MemoryRegion(Base, 2 * Page)], (address, buffer, count) =>
            {
                var actual = first ? 17 : count;
                first = false;
                Array.Fill(buffer, (byte)0x44, 0, actual);
                return actual;
            });
        Assert.All(image, value => Assert.Equal(0x44, value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(536870913)]
    public void RejectsInvalidSizesBeforeRead(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StructuralUnitPoolScanner.ReadImage(Base, size,
            [], (_, _, _) => throw new Exception("Unexpected read")));
    }

    [Fact]
    public void RejectsOverflowAndNonUserModuleBounds()
    {
        foreach (var address in new[] { 0UL, ulong.MaxValue, 0x00007FFFFFFFFFFFUL })
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadOnlyProcessMemory.ValidateModuleBounds(address, Page));
    }

    [Fact]
    public void RejectsMalformedQueriesAndQueryFailure()
    {
        ReadOnlyProcessMemory.ModuleRegionInfo?[] invalid =
        [null, Info(Base, 0), Info(Base + 1, Page), Info(Base - Page, Page), Info(Base, ulong.MaxValue)];
        foreach (var result in invalid)
            Assert.Throws<InvalidDataException>(() => ReadOnlyProcessMemory.EnumerateReadableModuleRegions(
                Base, Page, _ => result).ToArray());
    }

    [Fact]
    public void RejectsInvalidRangesBeforeReading()
    {
        foreach (var range in new[] { new MemoryRegion(Base - 1, Page), new MemoryRegion(Base, Page + 1),
                     new MemoryRegion(Base, 0), new MemoryRegion(ulong.MaxValue, 1) })
            Assert.Throws<InvalidDataException>(() => StructuralUnitPoolScanner.ReadImage(Base, Page,
                [range], (_, _, _) => throw new Exception("Unexpected read")));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4097)]
    public void RejectsImpossibleActualByteCounts(int actual)
    {
        Assert.Throws<InvalidDataException>(() => StructuralUnitPoolScanner.ReadImage(Base, Page,
            [new MemoryRegion(Base, Page)], (_, _, _) => actual));
    }

    [Fact]
    public void EnforcesReadAndQueryBudgets()
    {
        var reads = 0;
        Assert.Throws<InvalidDataException>(() => StructuralUnitPoolScanner.ReadImage(Base, Page,
            [new MemoryRegion(Base, Page)], (_, _, _) => { reads++; return 1; }));
        Assert.Equal(1027, reads);
        var queries = 0;
        Assert.Throws<InvalidDataException>(() => ReadOnlyProcessMemory.EnumerateReadableModuleRegions(
            Base, 200000, address => { queries++; return Info(address, 1); }).ToArray());
        Assert.Equal(131074, queries);
    }

    [Fact]
    public void CancellationInterruptsQueriesReadsAndRttiScan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ReadOnlyProcessMemory.EnumerateReadableModuleRegions(
            Base, Page, _ => throw new Exception("Unexpected query"), cancellation.Token).ToArray());
        Assert.Throws<OperationCanceledException>(() => StructuralUnitPoolScanner.ReadImage(Base, Page,
            [], (_, _, _) => throw new Exception("Unexpected read"), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => StructuralUnitPoolScanner.FindClassVftables(
            new byte[Page], Base, ".?AVCUnit@@", cancellation.Token));
    }

    [Fact]
    public void UnreadableImageFailsClosedAndVftablesAreNotStaticallyCached()
    {
        Assert.Empty(StructuralUnitPoolScanner.ReadImage(Base, Page, [new MemoryRegion(Base, Page)], (_, _, _) => 0));
        Assert.DoesNotContain(typeof(StructuralUnitPoolScanner).GetFields(BindingFlags.NonPublic | BindingFlags.Static),
            field => field.Name.Contains("vftable", StringComparison.OrdinalIgnoreCase));
    }
}
