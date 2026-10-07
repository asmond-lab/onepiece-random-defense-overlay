using System.Security.Cryptography;
using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RuntimeMapIdentityProviderTests
{
    private static readonly DateTimeOffset ProcessStart =
        new(2026, 8, 27, 16, 8, 59, TimeSpan.FromHours(9));

    [Fact]
    public void FreshCompleteOpeningRecordProvesExactArchiveBytes()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n");

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityState.Proven, result.State);
        Assert.Equal(RuntimeMapIdentityFailure.None, result.Failure);
        Assert.Equal(Path.GetFullPath(fixture.ArchivePath), result.ActualArchivePath);
        Assert.Equal(fixture.Pin.LengthBytes, result.ArchiveBytesRead);
        Assert.Equal(fixture.Pin.Sha256, result.ActualArchiveSha256);
        Assert.Equal(new FileInfo(fixture.LogPath).Length, result.LogBytesRead);
        Assert.Equal(1, result.LogReadCalls);
        Assert.Equal(1, result.ArchiveReadCalls);
    }

    [Fact]
    public void MissingLogIsUnknown()
    {
        using var fixture = Fixture.Create();

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityFailure.LogMissing, result.Failure);
    }

    [Theory]
    [InlineData(100, 99, false)]
    [InlineData(100, 100, true)]
    public void RotationOrShrinkIsRejected(
        long beforeLength, long afterLength, bool creationChanged)
    {
        var creation = new DateTime(2026, 8, 27, 7, 0, 0, DateTimeKind.Utc);

        var failure = RuntimeMapIdentityProvider.ValidateLogContinuity(
            creation, beforeLength,
            creationChanged ? creation.AddTicks(1) : creation, afterLength);

        Assert.Equal(RuntimeMapIdentityFailure.LogRotated, failure);
    }

    [Fact]
    public void StalePreStartRecordIsUnknown()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:08:58.999  Opening map - " + fixture.ArchivePath + "\n");

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityState.Unknown, result.State);
        Assert.Equal(RuntimeMapIdentityFailure.NoPostStartOpeningRecord, result.Failure);
    }

    [Fact]
    public void MissingArchiveIsUnknown()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n");
        File.Delete(fixture.ArchivePath);

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityFailure.ArchiveMissing, result.Failure);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("hash")]
    public void WrongArchiveBytesAreUnknown(string mutation)
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n");
        if (mutation == "length") File.AppendAllText(fixture.ArchivePath, "x");
        else File.WriteAllBytes(fixture.ArchivePath,
            new byte[checked((int)fixture.Pin.LengthBytes)]);

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(mutation == "length"
                ? RuntimeMapIdentityFailure.ArchiveLengthMismatch
                : RuntimeMapIdentityFailure.ArchiveHashMismatch,
            result.Failure);
    }

    [Fact]
    public void LabelOnlyRelativePathIsUnknown()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - ORDR_S2_2.314[R].w3x\n");

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityFailure.OpeningMapPathNotAbsolute, result.Failure);
    }

    [Fact]
    public void ConflictingRecordsAtLatestTimestampAreAmbiguous()
    {
        using var fixture = Fixture.Create();
        var other = Path.Combine(fixture.Root, "other.w3x");
        File.Copy(fixture.ArchivePath, other);
        fixture.WriteLog(
            "8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n" +
            "8/27 16:10:27.355  Opening map - " + other + "\n");

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityFailure.AmbiguousLatestOpeningRecord, result.Failure);
    }

    [Fact]
    public void UniqueLatestCompleteRecordWinsOverEarlierPostStartRecord()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog(
            "8/27 16:09:30.000  Opening map - C:/missing/older.w3x\n" +
            "8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n");

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityState.Proven, result.State);
        Assert.Equal(Path.GetFullPath(fixture.ArchivePath), result.ActualArchivePath);
    }

    [Fact]
    public void PartialFinalOpeningRecordIsUnknown()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - " + fixture.ArchivePath);

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityFailure.PartialOpeningRecord, result.Failure);
    }

    [Fact]
    public void LaterSessionBoundaryReprobesAndRejectsPriorRecord()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n");

        var first = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);
        var afterReset = RuntimeMapIdentityProvider.Probe(
            ProcessStart.AddMinutes(2), fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityState.Proven, first.State);
        Assert.Equal(RuntimeMapIdentityFailure.NoPostStartOpeningRecord, afterReset.Failure);
    }

    [Fact]
    public void TailReadIsBounded()
    {
        using var fixture = Fixture.Create();
        var prefix = new string('x', RuntimeMapIdentityProvider.MaximumLogTailBytes + 1000);
        fixture.WriteLog(prefix + "\n8/27 16:10:27.355  Opening map - " +
                         fixture.ArchivePath + "\n");

        var result = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);

        Assert.Equal(RuntimeMapIdentityState.Proven, result.State);
        Assert.InRange(result.LogBytesRead, 1,
            RuntimeMapIdentityProvider.MaximumLogTailBytes);
    }

    [Fact]
    public void LiveReadinessRequiresCapabilityAndProvenCurrentIdentity()
    {
        using var fixture = Fixture.Create();
        fixture.WriteLog("8/27 16:10:27.355  Opening map - " + fixture.ArchivePath + "\n");
        var dataDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "../../../../Data"));
        var profile = RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(dataDirectory);
        var proven = RuntimeMapIdentityProvider.Probe(
            ProcessStart, fixture.LogPath, fixture.Pin);
        var unknown = RuntimeMapIdentityProvider.Probe(
            ProcessStart.AddMinutes(2), fixture.LogPath, fixture.Pin);

        Assert.True(RuntimeAdaptivePlanningReadiness.IsReady(profile, proven));
        Assert.False(RuntimeAdaptivePlanningReadiness.IsReady(profile, unknown));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string LogPath { get; }
        public string ArchivePath { get; }
        public MapArchivePin Pin { get; }

        private Fixture(string root, string logPath, string archivePath, MapArchivePin pin) =>
            (Root, LogPath, ArchivePath, Pin) = (root, logPath, archivePath, pin);

        public static Fixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"orand-map-identity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var archivePath = Path.Combine(root, "map.w3x");
            var bytes = Encoding.UTF8.GetBytes("pinned-map-bytes");
            File.WriteAllBytes(archivePath, bytes);
            var pin = new MapArchivePin("map.w3x", bytes.Length,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            return new Fixture(root, Path.Combine(root, "War3Log.txt"), archivePath, pin);
        }

        public void WriteLog(string text)
        {
            File.WriteAllText(LogPath, text);
            File.SetLastWriteTimeUtc(LogPath, ProcessStart.AddMinutes(3).UtcDateTime);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
