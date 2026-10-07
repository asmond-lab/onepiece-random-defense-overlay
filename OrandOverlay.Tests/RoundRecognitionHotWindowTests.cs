using System.Runtime.InteropServices;
using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RoundRecognitionHotWindowTests
{
    [Theory]
    [InlineData(7, 10)]
    [InlineData(9, 13)]
    [InlineData(15, 18)]
    [InlineData(43, 53)]
    public void NewerRoundWindowStillRescansAfterEightStaleCopies(int staleRound, int liveRound)
    {
        const int windowBytes = 4096;
        var scanner = new IncrementalMapStateScanner(windowBytes);
        var windows = Enumerable.Range(0, 24)
            .Select(_ => Marshal.AllocHGlobal(windowBytes)).ToArray();
        try
        {
            for (var i = 0; i < windows.Length; i++)
            {
                var round = i == 8 ? liveRound - 1 : staleRound;
                var bytes = RoundWindow(round, windowBytes);
                Marshal.Copy(bytes, 0, windows[i], bytes.Length);
                scanner.Observe((ulong)windows[i], bytes.Length, MapStateReader.ScanBuffer(bytes));
            }

            var updated = RoundWindow(liveRound, windowBytes);
            Marshal.Copy(updated, 0, windows[8], updated.Length);
            using var memory = ReadOnlyProcessMemory.Open(Environment.ProcessId);
            // Each hot read consumes the entire budget, so cold discovery cannot mask
            // the regression by finding the updated string elsewhere in this process.
            for (var i = 0; i < 8; i++)
            {
                scanner.ScanStep(memory, CancellationToken.None, hotRescanEverySteps: 1);
                Assert.Equal(windowBytes, scanner.LastBytesRead);
            }

            Assert.Equal(liveRound, scanner.Current.MaxRound);
            Assert.Equal(8, scanner.HotWindowCount);
        }
        finally
        {
            foreach (var window in windows) Marshal.FreeHGlobal(window);
        }
    }

    private static byte[] RoundWindow(int round, int size)
    {
        var bytes = new byte[size];
        Encoding.UTF8.GetBytes($"현재 라운드 : |r{round}|r").CopyTo(bytes, 0);
        return bytes;
    }
}
