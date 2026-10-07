using System.Security.Cryptography;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class NativeMapArchiveBindingTests
{
    private static readonly byte[] Data = { 1, 2, 3, 4, 5 };
    private static string Hash => Convert.ToHexString(SHA256.HashData(Data));
    private static NativeArchiveBindingResult Bind(NativeMapFixture f, FakeArchive file, NativeMapPathObservation? observation = null,
        long? size = null, string? hash = null, CancellationToken token = default) =>
        NativeMapArchiveBinding.BindCore(observation ?? f.Observe().Observation!, size ?? Data.Length, hash ?? Hash, _ => file, token);
    [Fact] public void BoundWordingDoesNotClaimLoadedSource()
    {
        var f = new NativeMapFixture();
        // SYNTHETIC proof only: this flag is not established by the real-game probe.
        f.Context = f.Context with { ExperimentalActiveMapPathSemanticsVerified = true };
        using var file = new FakeArchive(f);
        var result = Bind(f, file);
        Assert.Equal(NativeArchiveBindingState.NativeArchiveBound, result.State);
        Assert.Equal("active native map-setup path resolves to pinned archive bytes", result.Reason);
        Assert.DoesNotContain("JASS", result.Reason); Assert.True(file.Disposed);
    }
    [Theory][InlineData("length")][InlineData("hash")][InlineData("mtime")][InlineData("reparse")]
    [InlineData("identity")][InlineData("metadata")][InlineData("native")][InlineData("path")][InlineData("short")]
    public void CorruptionChangesAndUnsupportedMtimeFailClosed(string mode)
    {
        var f = new NativeMapFixture(); var observation = f.Observe().Observation!;
        using var file = new FakeArchive(f);
        if (mode == "length") file.Value = file.Value with { Length = 9 };
        if (mode == "mtime") file.Value = file.Value with { LastWrite = f.Context.ProcessStartedAt.AddSeconds(1).UtcDateTime.ToFileTimeUtc() };
        if (mode == "reparse") file.Value = file.Value with { Attributes = 0x400 };
        if (mode == "identity") file.OnSnapshot = () => { if (file.Snapshots > 1) file.Value = file.Value with { FileId = 99 }; };
        if (mode == "metadata") file.OnSnapshot = () => { if (file.Snapshots > 1) file.Value = file.Value with { LastWrite = file.Value.LastWrite - 1 }; };
        if (mode == "native") file.OnSnapshot = () => { if (file.Snapshots > 1) f.Context = f.Context with { Epoch = Guid.NewGuid() }; };
        if (mode == "path") file.OnSnapshot = () => { if (file.Snapshots > 1) f.SetText(System.Text.Encoding.UTF8.GetBytes("C:/Maps/other.w3x")); };
        if (mode == "short") file.Stream.SetLength(2);
        var result = Bind(f, file, observation, hash: mode == "hash" ? new string('0', 64) : null);
        Assert.Equal(NativeArchiveBindingState.Unknown, result.State);
        if (mode == "mtime") Assert.Equal("UnsupportedArchiveModifiedAfterProcessStart", result.Reason);
    }
    [Fact] public void ExpiredObservationNeverOpensFile()
    {
        var f = new NativeMapFixture(); var o = f.Observe().Observation!; f.Now += 3001; bool opened = false;
        var r = NativeMapArchiveBinding.BindCore(o, Data.Length, Hash, _ => { opened = true; throw new IOException(); }, default);
        Assert.Equal("ObservationExpired", r.Reason); Assert.False(opened);
    }
    [Fact] public void IOErrorsAreUnknown()
    {
        var f = new NativeMapFixture(); var o = f.Observe().Observation!;
        foreach (var ex in new Exception[] { new IOException("io"), new UnauthorizedAccessException("denied") })
        {
            var r = NativeMapArchiveBinding.BindCore(o, Data.Length, Hash, _ => throw ex, default);
            Assert.Equal(NativeArchiveBindingState.Unknown, r.State); Assert.Equal(ex.Message, r.Reason);
        }
    }
    [Fact] public void CancelBeforeOpenAndDuringHashFailClosed()
    {
        var f = new NativeMapFixture(); var o = f.Observe().Observation!; using var c = new CancellationTokenSource();
        using var file = new FakeArchive(f); c.Cancel();
        Assert.Equal("Cancelled", Bind(f, file, o, token: c.Token).Reason);
        using var c2 = new CancellationTokenSource(); using var file2 = new FakeArchive(f);
        file2.OnSnapshot = () => c2.Cancel();
        Assert.Equal("Cancelled", Bind(f, file2, o, token: c2.Token).Reason);
    }
    [Fact] public void TimeSpentInIOCannotReturnSuccessAfterDeadline()
    {
        var f = new NativeMapFixture(); var o = f.Observe().Observation!; using var file = new FakeArchive(f);
        file.OnSnapshot = () => f.Now += 3001;
        Assert.Equal("ObservationExpired", Bind(f, file, o).Reason);
    }
    [Fact] public void ObservationHasNoPublicConstructor()
    { Assert.Empty(typeof(NativeMapPathObservation).GetConstructors()); }
    [Fact] public void UnknownActiveSemanticsStillAllowsStructuralObservationAndHashMatch()
    {
        var f = new NativeMapFixture(); Assert.False(f.Context.ExperimentalActiveMapPathSemanticsVerified);
        var observation = f.Observe().Observation; Assert.NotNull(observation);
        using var file = new FakeArchive(f); var result = Bind(f, file, observation);
        Assert.Equal(NativeArchiveBindingState.StructuralPathArchiveMatch, result.State);
        Assert.Equal(NativeMapArchiveBinding.StructuralMeaning, result.Reason);
        Assert.NotEqual(NativeMapArchiveBinding.BoundMeaning, result.Reason);
        Assert.Equal(Data.Length, result.ArchiveBytesRead);
    }
    [Fact] public void ExactThreeSecondExpiryNeverOpensFile()
    {
        var f = new NativeMapFixture(); var o = f.Observe().Observation!; f.Now += 3000; bool opened = false;
        var r = NativeMapArchiveBinding.BindCore(o, Data.Length, Hash, _ => { opened = true; throw new IOException(); }, default);
        Assert.Equal("ObservationExpired", r.Reason); Assert.False(opened);
    }
    [Theory][InlineData(false, false)][InlineData(true, false)][InlineData(true, true)]
    public void WindowsRealTemporaryArchiveUsesProductionFactory(bool forwardSlashes, bool badHash)
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "native-map-binding-" + Guid.NewGuid().ToString("N"));
        string nested = System.IO.Path.Combine(directory, "maps");
        Directory.CreateDirectory(nested);
        string archive = System.IO.Path.Combine(nested, "synthetic.w3x");
        try
        {
            File.WriteAllBytes(archive, Data);
            File.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddMinutes(-2));
            var f = new NativeMapFixture { ClockOverride = () => Environment.TickCount64 };
            // SYNTHETIC native fixture and semantics attestation. Never reads any game/custom map.
            f.Context = f.Context with { ProcessId = 1, ProcessStartedAt = DateTimeOffset.UtcNow,
                ExperimentalActiveMapPathSemanticsVerified = true };
            string nativePath = forwardSlashes ? archive.Replace((char)92, '/') : archive;
            f.SetText(System.Text.Encoding.UTF8.GetBytes(nativePath));
            Assert.Equal(0x81u, NativeMapArchiveBinding.LocalFileAccess);
            Assert.Equal(0x00200000u, NativeMapArchiveBinding.LocalFileFlags);
            Assert.Equal(1u, NativeMapArchiveBinding.LocalShareMode);
            // SAME factory used by public Bind: successful open exercises final-path normalization,
            // OPEN_REPARSE_POINT, read-only access and 128-bit FileIdInfo on a real Windows file.
            using (var held = NativeMapArchiveBinding.OpenLocalArchive(nativePath))
            {
                var before = held.Snapshot();
                Assert.Equal(Data.Length, before.Length);
                Assert.True(before.FileId != 0 || before.FileIdHigh != 0);
                Assert.Equal(0u, before.Attributes & 0x410u);
                Assert.True(DateTime.FromFileTimeUtc(before.LastWrite) <= f.Context.ProcessStartedAt.UtcDateTime);
                Assert.True(held.Content.CanRead); Assert.False(held.Content.CanWrite);
                Assert.Equal(before, held.Snapshot());
                Assert.ThrowsAny<IOException>(() => { using var writer = new FileStream(archive, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); });
                var moveError = Record.Exception(() => Directory.Move(nested, nested + "-renamed"));
                Assert.True(moveError is IOException or UnauthorizedAccessException, "Held ancestor must reject rename/delete sharing.");
            }
            var observation = f.Observe().Observation!;
            var result = NativeMapArchiveBinding.BindCore(observation, Data.Length,
                badHash ? new string('0', 64) : Hash, NativeMapArchiveBinding.OpenLocalArchive, default);
            Assert.Equal(badHash ? NativeArchiveBindingState.Unknown : NativeArchiveBindingState.NativeArchiveBound, result.State);
            Assert.Equal(badHash ? "ArchiveHashMismatch" : NativeMapArchiveBinding.BoundMeaning, result.Reason);
            if (!badHash) Assert.Equal(Hash.ToLowerInvariant(), result.Sha256);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class FakeArchive : NativeMapArchiveBinding.IArchive
    {
        internal MemoryStream Stream = new(); internal bool Disposed; internal int Snapshots;
        internal NativeMapArchiveBinding.Identity Value; internal Action? OnSnapshot;
        internal FakeArchive(NativeMapFixture f)
        { Stream.Write(Data); Stream.Position = 0; long old = f.Context.ProcessStartedAt.AddDays(-1).UtcDateTime.ToFileTimeUtc(); Value = new(1, 2, Data.Length, old, old, 0); }
        public Stream Content => Stream;
        public NativeMapArchiveBinding.Identity Snapshot() { Snapshots++; OnSnapshot?.Invoke(); return Value; }
        public void Dispose() { Disposed = true; Stream.Dispose(); }
    }
}
