using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RoundRecognitionDiscoveryTests
{
    private const int Budget = 4 * 1024 * 1024;

    [Fact]
    public void InitialRoundBehindLargeAllocationsIsFoundWithinOneBudget()
    {
        var image = new MemoryImage();
        image.Regions.Add(new(0x10000, 1800UL * 1024 * 1024));
        image.Regions.Add(new(0x80000000, 256 * 1024));
        image.Regions.Add(new(0x100000000, 400UL * 1024 * 1024));
        image.Round(0x8003F000, 53);
        var scanner = new IncrementalMapStateScanner(Budget);

        Step(scanner, image);

        Assert.Equal(53, scanner.Current.MaxRound);
        Assert.Equal(Budget, scanner.LastBytesRead);
    }

    [Theory]
    [InlineData(Budget, 53, 54)]
    [InlineData(128 * 1024, 60, 61)]
    public void NewlyAllocatedRoundSourcePreemptsUnfinishedHeapSweep(int budget, int before, int after)
    {
        var image = new MemoryImage();
        image.Regions.Add(new(0x10000, 1800UL * 1024 * 1024));
        image.Regions.Add(new(0x80000000, 64 * 1024));
        image.Round(0x80001000, before);
        var scanner = new IncrementalMapStateScanner(budget);
        Step(scanner, image);
        Assert.Equal(before, scanner.Current.MaxRound);

        // A lower-address allocation must also preempt, not just a growing heap tip.
        image.Regions.Add(new(0x78000000, 256 * 1024));
        image.Round(0x7803F000, after);
        for (var i = 0; i < 8; i++) Step(scanner, image);

        Assert.Equal(after, scanner.Current.MaxRound);
        Assert.All(image.StepBytes, bytes => Assert.InRange(bytes, 1, budget));
    }

    [Fact]
    public void LargeRegionsRemainCoveredWhileNewSourcesArePrioritized()
    {
        var image = new MemoryImage();
        image.Regions.Add(new(0x10000, 40UL * 1024 * 1024));
        image.Round(0x10000 + 30UL * 1024 * 1024, 53);
        var scanner = new IncrementalMapStateScanner(Budget);
        Step(scanner, image);
        image.Regions.Add(new(0x80000000, 64 * 1024));
        image.Round(0x80001000, 43);
        for (var i = 0; i < 12; i++) Step(scanner, image);

        Assert.Equal(53, scanner.Current.MaxRound);
    }

    [Fact]
    public void ResetForgetsAllocationHistoryAndPreviousRound()
    {
        var image = new MemoryImage();
        image.Regions.Add(new(0x10000, 64 * 1024));
        image.Round(0x11000, 53);
        var scanner = new IncrementalMapStateScanner(Budget);
        Step(scanner, image);
        scanner.Reset();
        image.Round(0x11000, 1);

        Step(scanner, image);

        Assert.Equal(1, scanner.Current.MaxRound);
    }

    private static void Step(IncrementalMapStateScanner scanner, MemoryImage image)
    {
        var before = image.BytesRead;
        scanner.ScanStep(() => image.Regions, image.Read, CancellationToken.None);
        var bytes = image.BytesRead - before;
        image.StepBytes.Add(bytes);
        Assert.Equal(bytes, scanner.LastBytesRead);
    }

    private sealed class MemoryImage
    {
        public List<MemoryRegion> Regions { get; } = [];
        public List<int> StepBytes { get; } = [];
        public int BytesRead { get; private set; }
        private readonly Dictionary<ulong, byte[]> strings = [];

        public void Round(ulong address, int value) =>
            strings[address] = Encoding.UTF8.GetBytes($"현재 라운드 : |r{value}|r");

        public int Read(ulong address, byte[] buffer, int length)
        {
            Assert.Contains(Regions, region => address >= region.BaseAddress &&
                address + (ulong)length <= region.BaseAddress + region.Size);
            buffer.AsSpan(0, length).Clear();
            foreach (var (start, bytes) in strings)
            {
                var first = Math.Max(address, start);
                var end = Math.Min(address + (ulong)length, start + (ulong)bytes.Length);
                if (first < end)
                    bytes.AsSpan((int)(first - start), (int)(end - first))
                        .CopyTo(buffer.AsSpan((int)(first - address)));
            }
            BytesRead += length;
            return length;
        }
    }
}
