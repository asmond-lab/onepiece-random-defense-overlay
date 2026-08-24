using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class MapStateReaderPerformanceTests
{
    [Fact]
    public void WarcraftRecognitionCadenceDoesNotRunFasterThanOncePerTwoSeconds()
    {
        Assert.True(MainWindow.RecognitionInterval >= TimeSpan.FromSeconds(2),
            $"현재 인식 주기: {MainWindow.RecognitionInterval.TotalMilliseconds}ms");
    }

    [Fact]
    public void WarcraftRecognizerLimitsBackgroundMapScanToFourMegabytes()
    {
        Assert.InRange(WarcraftMemoryRecognitionService.MapStateBackgroundBudgetBytes,
            1, 4 * 1024 * 1024);
    }

    [Fact]
    public void WarcraftRecognizerLimitsEndgameMapScanTo128Kilobytes()
    {
        Assert.InRange(WarcraftMemoryRecognitionService.MapStateEndgameBudgetBytes,
            1, 128 * 1024);
    }

    [Fact]
    public void IncrementalScanHonorsPerStepByteBudget()
    {
        const int budget = 1024 * 1024;
        var scanner = new IncrementalMapStateScanner(budget);
        using var memory = ReadOnlyProcessMemory.Open(Environment.ProcessId);

        _ = scanner.ScanStep(memory, CancellationToken.None);

        Assert.InRange(scanner.LastBytesRead, 1, budget);
    }

    [Fact]
    public void IncrementalScanResetClearsPreviousMatchState()
    {
        var scanner = new IncrementalMapStateScanner(1024 * 1024);
        scanner.Merge(new MapStateSample(65, 2, "신"));

        scanner.Reset();

        Assert.Equal(new MapStateSample(0, 0, "unknown"), scanner.Current);
        Assert.Equal(0, scanner.LastBytesRead);
    }

    [Fact]
    public void IncrementalScanRemembersSignalWindowsAndResetClearsThem()
    {
        var scanner = new IncrementalMapStateScanner(1024 * 1024);
        var sample = new MapStateSample(65, 1, "신");

        scanner.Observe(0x10000, 0x4000, sample);
        Assert.Equal(sample, scanner.Current);
        scanner.Observe(0x10000, 0x4000, sample);

        Assert.Equal(1, scanner.HotWindowCount);
        scanner.Reset();
        Assert.Equal(0, scanner.HotWindowCount);
    }

    [Fact]
    public void FullPrivateMemoryScanReusesBoundedBuffer()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();

        using var memory = ReadOnlyProcessMemory.Open(Environment.ProcessId);
        var sample = MapStateReader.TryRead(memory, CancellationToken.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(sample);
        Assert.True(allocated < 32L * 1024 * 1024,
            $"맵 상태 한 번 스캔이 {allocated / 1024d / 1024d:F1}MB를 할당했습니다.");
    }
}
