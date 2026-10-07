using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322MapIdentityHotPathTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 19, 37, 41, TimeSpan.FromHours(9));

    [Fact]
    public void SessionReportsEveryCompletedOuterAndHeldTailRead()
    {
        using var f = new Fixture();
        f.Open();
        var expected = new FileInfo(f.LogPath).Length;
        var cold = f.Probe();
        Assert.Equal(RuntimeMapIdentityState.Proven, cold.State);
        Assert.Equal(3 * expected, cold.LogBytesRead);
        Assert.Equal(3, cold.LogReadCalls);
        Assert.Equal(1, cold.ArchiveReadCalls);
        var warm = f.Probe();
        Assert.Equal(RuntimeMapIdentityState.Proven, warm.State);
        Assert.Equal(2 * expected, warm.LogBytesRead);
        Assert.Equal(2, warm.LogReadCalls);
        Assert.Equal(0, warm.ArchiveReadCalls);
    }

    [Fact]
    public void LocalTimeBoundaryRejectsStaleRecordThatUtcRelabelWouldAdmit()
    {
        using var f = new Fixture();
        f.Log("9/24 19:37:40.999  Opening map - " + f.Archive + "\n");
        Assert.Equal(RuntimeMapIdentityFailure.NoPostStartOpeningRecord, f.Probe().Failure);
        Assert.Equal(RuntimeMapIdentityFailure.LogClockUnverified,
            f.Probe(start: Start.ToUniversalTime()).Failure);
        // Demonstrates the former caller's UTC conversion error, not an approved map.
        Assert.Equal(RuntimeMapIdentityState.Proven,
            RuntimeMapIdentityProvider.Probe(Start.ToUniversalTime(), f.LogPath, f.Pin).State);
    }

    [Fact]
    public void ColdThenWarmReusesLockedArchiveAndRejectsRestoredTimestampMutation()
    {
        using var f = new Fixture();
        f.Open();
        Assert.Equal(f.Pin.LengthBytes, f.Probe().ArchiveBytesRead);
        var warm = f.Probe();
        Assert.Equal(RuntimeMapIdentityState.Proven, warm.State);
        Assert.Equal(0, warm.ArchiveReadCalls);
        Assert.Equal(0, warm.ArchiveBytesRead);
        Assert.Equal(2, warm.LogReadCalls);
        var writeTime = File.GetLastWriteTimeUtc(f.Archive);
        Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(f.Archive,
            Encoding.UTF8.GetBytes("other-map-bytes!")));
        Assert.Equal(RuntimeMapIdentityState.Proven, f.Probe().State);
        f.Session.Reset();
        File.WriteAllBytes(f.Archive, Encoding.UTF8.GetBytes("other-map-bytes!"));
        File.SetLastWriteTimeUtc(f.Archive, writeTime);
        var mismatch = f.Probe();
        Assert.Equal(RuntimeMapIdentityFailure.ArchiveHashMismatch, mismatch.Failure);
        Assert.Equal(1, mismatch.ArchiveReadCalls);
        Assert.Equal(f.Pin.LengthBytes, mismatch.ArchiveBytesRead);
    }

    [Fact]
    public void SessionPinRecordLogAndPathTransitionsNeverReuseStaleProof()
    {
        using var f = new Fixture();
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        Assert.Equal(0, f.Probe().ArchiveReadCalls);
        Assert.Equal(1, f.Probe(pid: 43).ArchiveReadCalls);
        Assert.Equal(1, f.Probe(ticks: 999).ArchiveReadCalls);
        Assert.Equal(1, f.Probe(pin: f.Pin with { FileName = "different", Sha256 = f.Pin.Sha256.ToUpperInvariant() }).ArchiveReadCalls);
        f.Session.Reset();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        File.AppendAllText(f.LogPath, "unrelated line\n");
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        f.Log("9/24 19:37:43.000  Opening map - " + f.Archive + "\n");
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        f.Log("9/24 19:37:44.000  Opening map - C:/missing/bad.w3x\n");
        Assert.Equal(RuntimeMapIdentityFailure.ArchiveMissing, f.Probe().Failure);
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        f.Log("9/24 19:37:43.000  Opening map - " + f.Archive);
        Assert.Equal(RuntimeMapIdentityFailure.PartialOpeningRecord, f.Probe().Failure);
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        f.Log("9/24 19:37:43.000  Opening map - " + f.Archive + "\n" +
              "9/24 19:37:43.000  Opening map - C:/other/alias.w3x\n");
        Assert.Equal(RuntimeMapIdentityFailure.AmbiguousLatestOpeningRecord, f.Probe().Failure);
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        var alias = Path.Combine(f.Root, "alias.w3x");
        File.Copy(f.Archive, alias);
        f.Log("9/24 19:37:43.000  Opening map - " + alias + "\n");
        Assert.Equal(1, f.Probe().ArchiveReadCalls); // new path must rehash even identical bytes
        f.Log("9/24 19:37:43.000  Opening map - " + f.Archive + "\n");
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        f.Session.Reset();
        File.AppendAllText(f.Archive, "x");
        Assert.Equal(RuntimeMapIdentityFailure.ArchiveLengthMismatch, f.Probe().Failure);
    }

    [Fact]
    public void RotatedOrMissingLogAndNewBoundaryRejectProof()
    {
        using var f = new Fixture();
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        Assert.Equal(RuntimeMapIdentityFailure.LogClockUnverified,
            f.Probe(start: Start.AddMinutes(2)).Failure);
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        f.Log("short\n");
        Assert.Equal(RuntimeMapIdentityFailure.NoPostStartOpeningRecord, f.Probe().Failure);
        f.Open();
        Assert.Equal(1, f.Probe().ArchiveReadCalls);
        // Held log disallows delete/rename; release first to model rotation.
        Assert.ThrowsAny<IOException>(() => File.Delete(f.LogPath));
        f.Session.Reset();
        File.Delete(f.LogPath);
        Assert.Equal(RuntimeMapIdentityFailure.LogMissing, f.Probe().Failure);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "orand-map-hotpath-" + Guid.NewGuid().ToString("N"));
        public readonly RuntimeMapIdentitySession Session = new();
        public string Archive => Path.Combine(Root, "map.w3x");
        public string LogPath => Path.Combine(Root, "War3Log.txt");
        public MapArchivePin Pin { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            var bytes = Encoding.UTF8.GetBytes("pinned-map-bytes");
            File.WriteAllBytes(Archive, bytes);
            Pin = new MapArchivePin("map.w3x", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        public void Open() => Log("9/24 19:37:43.000  Opening map - " + Archive + "\n");
        public void Log(string text) => File.WriteAllText(LogPath,
            "9/24 19:37:41.001  GameMain Started\n" + text);
        public RuntimeMapIdentityResult Probe(int pid = 42, long ticks = 123,
            MapArchivePin? pin = null, DateTimeOffset? start = null) =>
            RuntimeMapIdentityProvider.Probe(start ?? Start, LogPath, pin ?? Pin, Session, pid, ticks);
        public void Dispose() { Session.Dispose(); Directory.Delete(Root, true); }
    }
}
