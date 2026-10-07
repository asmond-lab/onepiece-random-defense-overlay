using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RoundSessionSourceTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(null, false)]
    [InlineData(2, true)]
    [InlineData(null, true)]
    public void PreviousGame48CannotOverrideCurrentSession2OrUnknown(int? confirmedRound, bool intactOldTitle)
    {
        // Exact damaged old-title prefix observed at 0x1a446687a5a in PID 20404.
        // Even an intact title is not proof that its timer belongs to this match.
        var prefix = intactOldTitle ? "|cffFF0000" : "0\0ffFF0000";
        var bytes = Encoding.UTF8.GetBytes(prefix + "현재 라운드|r : 48|r\0|cffFF0000현재 라운드|r : 2|r\0");
        var scanner = new IncrementalMapStateScanner(4096);
        MapStateSample Scan() => scanner.ScanStep(() => new[] { new MemoryRegion(0x10000, 4096) },
            (address, buffer, length) =>
            {
                buffer.AsSpan(0, length).Clear();
                bytes.CopyTo(buffer, 0);
                return length;
            }, CancellationToken.None);
        Scan();
        Assert.Equal(48, scanner.Current.MaxRound);
        var current = MapStateReader.SelectCurrentRound(scanner.Current, confirmedRound);
        Assert.Equal(confirmedRound ?? 0, current.MaxRound);
        scanner.Reset();
        Scan();
        Assert.Equal(confirmedRound ?? 0,
            MapStateReader.SelectCurrentRound(scanner.Current, confirmedRound).MaxRound);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    [InlineData(65)]
    public void ConfirmedCurrentSourceKeepsExactBossAndLateAttachmentRound(int round)
    {
        var heap = new MapStateSample(48, 3, "악몽");
        Assert.Equal(new MapStateSample(round, 3, "악몽"),
            MapStateReader.SelectCurrentRound(heap, round));
    }
}
