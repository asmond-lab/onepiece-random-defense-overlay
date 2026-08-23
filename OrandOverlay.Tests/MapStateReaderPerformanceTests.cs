using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class MapStateReaderPerformanceTests
{
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
