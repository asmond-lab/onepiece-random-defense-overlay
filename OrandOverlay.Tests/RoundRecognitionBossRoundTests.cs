using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RoundRecognitionBossRoundTests
{
    // Pinned war3map.j b4w (63997-64006) uses a distinct timer title through round 50.
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    public void SourceAuthoredBossTimerRecognizesRound(int round)
    {
        var bytes = Encoding.UTF8.GetBytes($"|cffFF0000보스 라운드|r : {round}|r");
        Assert.Equal(round, MapStateReader.ScanBuffer(bytes).MaxRound);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(50)]
    public void ScannerDoesNotSkipBossBetweenNormalRounds(int bossRound)
    {
        const int size = 4096;
        const ulong address = 0x10000;
        var scanner = new IncrementalMapStateScanner(size);
        var titles = new[]
        {
            $"|cffFF0000현재 라운드|r : {bossRound - 1}|r",
            $"|cffFF0000보스 라운드|r : {bossRound}|r",
            bossRound == 50
                ? "|cffFF0000현재 라운드 : |r51|r"
                : $"|cffFF0000현재 라운드|r : {bossRound + 1}|r"
        };
        for (var i = 0; i < titles.Length; i++)
        {
            // Keep the preceding timer copy, as the real heap does. New title must win.
            var bytes = Encoding.UTF8.GetBytes(string.Join('\0', titles.Take(i + 1)));
            var sample = scanner.ScanStep(() => new[] { new MemoryRegion(address, size) },
                (start, buffer, length) =>
                {
                    Assert.Equal(address, start);
                    Assert.Equal(size, length);
                    buffer.AsSpan(0, length).Clear();
                    bytes.CopyTo(buffer, 0);
                    return length;
                }, CancellationToken.None, hotRescanEverySteps: 1);
            Assert.Equal(bossRound - 1 + i, sample.MaxRound);
            Assert.Equal(size, scanner.LastBytesRead);
        }
    }

    [Theory]
    [InlineData("|cffFF0000보스 라운드|r : \"+I2S(OG)+\"|r")]
    [InlineData("|cffFF0000보스 라운드|r : |r")]
    [InlineData("|cffFF0000보스 라운드|r : 201|r")]
    [InlineData("|cffFF0000보스 라운드|r : 999|r")]
    public void BossMarkerKeepsLiteralAndRangeRejection(string title)
    {
        Assert.Equal(0, MapStateReader.ScanBuffer(Encoding.UTF8.GetBytes(title)).MaxRound);
    }

    // QDG (20466-20475) retains the current-round title for post-50 bosses.
    [Theory]
    [InlineData(55)]
    [InlineData(60)]
    [InlineData(65)]
    public void PostFiftyBossTimerRemainsRecognized(int round)
    {
        var bytes = Encoding.UTF8.GetBytes($"|cffFF0000현재 라운드 : |r{round}|r");
        Assert.Equal(round, MapStateReader.ScanBuffer(bytes).MaxRound);
    }
}
