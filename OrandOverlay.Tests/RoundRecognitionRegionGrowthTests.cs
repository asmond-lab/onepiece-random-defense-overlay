using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RoundRecognitionRegionGrowthTests
{
    private const ulong ColdBase = 0x10000;
    private const ulong GrowingBase = 0x80000000;
    private const int GrowthBytes = 64 * 1024;
    private const int OverlapBytes = 0x2000;
    private const int RefreshSteps = 4;

    [Theory]
    [InlineData(WarcraftMemoryRecognitionService.MapStateBackgroundBudgetBytes, 53, 54)]
    [InlineData(WarcraftMemoryRecognitionService.MapStateEndgameBudgetBytes, 60, 61)]
    public void SameBaseGrowthFindsNewRoundBeforeLargeSweepFinishes(int budget, int before, int after)
    {
        var image = new MemoryImage();
        image.Round(GrowingBase, before);
        var scanner = new IncrementalMapStateScanner(
            WarcraftMemoryRecognitionService.MapStateBackgroundBudgetBytes);
        Step(scanner, image, budget);
        Assert.Equal(before, scanner.Current.MaxRound);

        image.Grow();
        image.Round(GrowingBase + GrowthBytes, after);
        for (var i = 0; i < RefreshSteps - 1; i++) Step(scanner, image, budget);
        Assert.Equal(before, scanner.Current.MaxRound);
        var lastColdRead = image.Reads.Last(read => read.BaseAddress < GrowingBase);
        var expectedColdCursor = lastColdRead.BaseAddress + lastColdRead.Size - OverlapBytes;
        image.Reads.Clear();

        Step(scanner, image, budget);

        Assert.Equal(after, scanner.Current.MaxRound);
        Assert.Equal(new MemoryRegion(GrowingBase + GrowthBytes - OverlapBytes,
            GrowthBytes + OverlapBytes), image.Reads[0]);
        Assert.Equal(expectedColdCursor, image.Reads[1].BaseAddress);
        Assert.Equal(2, scanner.HotWindowCount);

        // The newly discovered source must participate in the existing hot rotation.
        image.Round(GrowingBase + GrowthBytes, after + 1);
        for (var i = 0; i < 2 * RefreshSteps; i++) Step(scanner, image, budget);
        Assert.Equal(after + 1, scanner.Current.MaxRound);
        Assert.DoesNotContain(image.Reads, read =>
            read.BaseAddress < GrowingBase && read.BaseAddress + read.Size == ColdBase + image.ColdSize);
    }

    [Theory]
    [InlineData(WarcraftMemoryRecognitionService.MapStateBackgroundBudgetBytes)]
    [InlineData(WarcraftMemoryRecognitionService.MapStateEndgameBudgetBytes)]
    public void UnchangedExtentDoesNotQueueTheSameSuffixAgain(int budget)
    {
        var image = new MemoryImage();
        var scanner = new IncrementalMapStateScanner(budget);
        Step(scanner, image, budget);
        image.Grow();
        image.Reads.Clear();

        for (var i = 0; i < 2 * RefreshSteps; i++) Step(scanner, image, budget);

        Assert.Equal(new MemoryRegion(GrowingBase + GrowthBytes - OverlapBytes,
            GrowthBytes + OverlapBytes),
            Assert.Single(image.Reads, read => read.BaseAddress >= GrowingBase));
        Assert.Equal(0, scanner.HotWindowCount);
    }

    [Fact]
    public void GrowthDiscoveryIncludesRoundSplitAcrossOldExtentBoundary()
    {
        var image = new MemoryImage();
        var scanner = new IncrementalMapStateScanner(GrowthBytes);
        Step(scanner, image, GrowthBytes);
        image.Grow();
        // Split the actual encoded marker, not an assumed process-memory offset.
        var markerBytes = Encoding.UTF8.GetByteCount("현재 라운드 : |r");
        image.Round(GrowingBase + GrowthBytes - (ulong)(markerBytes / 2), 54);

        for (var i = 0; i < RefreshSteps + 1; i++) Step(scanner, image, GrowthBytes);

        Assert.Equal(54, scanner.Current.MaxRound);
    }

    [Fact]
    public void ResetForgetsGrownExtentAndHotSourcesBeforeSameBaseIsReused()
    {
        const int budget = WarcraftMemoryRecognitionService.MapStateEndgameBudgetBytes;
        var image = new MemoryImage();
        var scanner = new IncrementalMapStateScanner(budget);
        Step(scanner, image, budget);
        image.Grow();
        image.Round(GrowingBase + GrowthBytes, 61);
        for (var i = 0; i < RefreshSteps; i++) Step(scanner, image, budget);
        Assert.Equal(61, scanner.Current.MaxRound);

        scanner.Reset();
        Assert.Equal(new MapStateSample(0, 0, "unknown"), scanner.Current);
        Assert.Equal(0, scanner.LastBytesRead);
        Assert.Equal(0, scanner.HotWindowCount);
        image = new MemoryImage();
        image.Round(GrowingBase, 1);
        Step(scanner, image, budget);
        Assert.Equal(1, scanner.Current.MaxRound);
        image.Grow();
        image.Round(GrowingBase + GrowthBytes, 2);

        for (var i = 0; i < RefreshSteps; i++) Step(scanner, image, budget);

        Assert.Equal(2, scanner.Current.MaxRound);
    }

    private static void Step(IncrementalMapStateScanner scanner, MemoryImage image, int budget)
    {
        var before = image.BytesRead;
        scanner.ScanStep(() => image.Regions, image.Read, CancellationToken.None,
            overrideBudget: budget);
        Assert.Equal(image.BytesRead - before, scanner.LastBytesRead);
        Assert.InRange(scanner.LastBytesRead, 1, budget);
    }

    private sealed class MemoryImage
    {
        public ulong ColdSize => 1800UL * 1024 * 1024;
        public List<MemoryRegion> Regions { get; } =
            [new(ColdBase, 1800UL * 1024 * 1024), new(GrowingBase, GrowthBytes)];
        public List<MemoryRegion> Reads { get; } = [];
        public long BytesRead { get; private set; }
        private readonly Dictionary<ulong, byte[]> strings = [];

        public void Grow() => Regions[1] = new(GrowingBase, Regions[1].Size + GrowthBytes);

        public void Round(ulong address, int round) =>
            strings[address] = Encoding.UTF8.GetBytes($"현재 라운드 : |r{round}|r");

        public int Read(ulong address, byte[] buffer, int length)
        {
            Assert.Contains(Regions, region => address >= region.BaseAddress &&
                address + (ulong)length <= region.BaseAddress + region.Size);
            Reads.Add(new(address, (ulong)length));
            BytesRead += length;
            buffer.AsSpan(0, length).Clear();
            foreach (var (start, bytes) in strings)
            {
                var first = Math.Max(address, start);
                var end = Math.Min(address + (ulong)length, start + (ulong)bytes.Length);
                if (first < end)
                    bytes.AsSpan((int)(first - start), (int)(end - first))
                        .CopyTo(buffer.AsSpan((int)(first - address)));
            }
            return length;
        }
    }
}
