using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OrandOverlay;

public enum NativeArchiveBindingState { Unknown, NativeArchiveBound, StructuralPathArchiveMatch }
public sealed record NativeArchiveBindingResult(NativeArchiveBindingState State, string Reason,
    string NativePath, string Sha256, long ArchiveBytesRead, string Provenance);

/// <summary>Not loaded-memory/JASS byte verification. Not used by existing production gates.</summary>
public static class NativeMapArchiveBinding
{
    public const string StructuralMeaning = "structural native map-setup path resolves to pinned archive bytes; active-world source semantics unverified";
    internal const uint LocalFileAccess = 0x81, LocalFileFlags = 0x00200000, LocalShareMode = 1;
    internal static IArchive OpenLocalArchive(string path) => new LocalArchive(path);
    public const string BoundMeaning = "active native map-setup path resolves to pinned archive bytes";
    public static NativeArchiveBindingResult Bind(NativeMapPathObservation observation, CancellationToken token = default)
    {
        try
        {
            observation.CheckFresh(token);
            // Single source of archive constants; no filename-based approval or arbitrary public pin.
            var pin = Map2320SourceMetadata.LoadBundled().Archive;
            return BindCore(observation, pin.LengthBytes, pin.Sha256, OpenLocalArchive, token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        { return Unknown(observation, ex is OperationCanceledException ? "Cancelled" : ex.Message, 0); }
    }
    internal sealed record Identity(ulong Volume, ulong FileId, long Length, long Created, long LastWrite, uint Attributes, ulong FileIdHigh = 0);
    internal interface IArchive : IDisposable { Stream Content { get; } Identity Snapshot(); }
    internal static NativeArchiveBindingResult BindCore(NativeMapPathObservation observation, long length, string hash,
        Func<string, IArchive> open, CancellationToken token)
    {
        long total = 0;
        try
        {
            if (observation is null) throw new InvalidDataException("MissingNativeObservation");
            observation.CheckFresh(token);
            if (!Warcraft300MapPathReader.IsSafeLocalMapPath(observation.Path)) throw new InvalidDataException("UnsafeMapPath");
            if (length <= 0 || hash.Length != 64) throw new InvalidDataException("InvalidSourcePin");
            observation.Revalidate(token);
            using var file = open(observation.Path);
            observation.CheckFresh(token);
            var before = file.Snapshot();
            if ((before.Attributes & 0x410) != 0) throw new InvalidDataException("ReparseOrDirectoryRejected");
            if (before.Length != length) throw new InvalidDataException("ArchiveLengthMismatch");
            if (DateTime.FromFileTimeUtc(before.LastWrite) > observation.Context.ProcessStartedAt.UtcDateTime)
                throw new InvalidDataException("UnsupportedArchiveModifiedAfterProcessStart");
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            while (total < length)
            {
                observation.CheckFresh(token);
                int n = file.Content.Read(buffer, 0, (int)Math.Min(buffer.Length, length - total));
                observation.CheckFresh(token);
                if (n <= 0) throw new InvalidDataException("ArchiveShortRead");
                sha.AppendData(buffer, 0, n); total += n;
            }
            observation.CheckFresh(token);
            if (file.Content.ReadByte() != -1) throw new InvalidDataException("ArchiveGrew");
            var after = file.Snapshot();
            if (before != after) throw new InvalidDataException("ArchiveIdentityOrMetadataChanged");
            string actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ArchiveHashMismatch");
            // File/ancestor read locks are still held during fresh native validation and final metadata check.
            observation.Revalidate(token);
            if (file.Snapshot() != before) throw new InvalidDataException("ArchiveChangedAfterNativeCheck");
            observation.CheckFresh(token);
            bool activeSemantics = observation.Context.ExperimentalActiveMapPathSemanticsVerified;
            return new(activeSemantics ? NativeArchiveBindingState.NativeArchiveBound : NativeArchiveBindingState.StructuralPathArchiveMatch,
                activeSemantics ? BoundMeaning : StructuralMeaning, observation.Path, actual, total, observation.Provenance);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        { return Unknown(observation, ex is OperationCanceledException ? "Cancelled" : ex.Message, total); }
    }
    private static NativeArchiveBindingResult Unknown(NativeMapPathObservation? observation, string reason, long bytes) =>
        new(NativeArchiveBindingState.Unknown, reason, observation?.Path ?? "", "", bytes, observation?.Provenance ?? "");

    // Read-only Windows I/O. No game handle duplication, seeking, or process APIs.
    private sealed class LocalArchive : IArchive
    {
        private readonly List<SafeFileHandle> parents = new();
        private FileStream? stream;
        public Stream Content => stream ?? throw new ObjectDisposedException(nameof(LocalArchive));
        public LocalArchive(string path)
        {
            try
            {
                if (!OperatingSystem.IsWindows()) throw new IOException("WindowsLocalFilesOnly");
                string full = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
                string root = Path.GetPathRoot(full) ?? throw new IOException("MissingDrive");
                if (new DriveInfo(root).DriveType != DriveType.Fixed) throw new IOException("NetworkOrNonFixedDriveRejected");
                var device = new StringBuilder(1024);
                if (QueryDosDevice(root.Substring(0, 2), device, device.Capacity) == 0 ||
                    !device.ToString().StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("RedirectedDriveRejected");
                // Hold each ancestor without write/delete sharing. Open reparse points themselves, never follow them.
                string current = root;
                var pieces = full.Substring(root.Length).Split(Path.DirectorySeparatorChar);
                for (int i = -1; i < pieces.Length - 1; i++)
                {
                    if (i >= 0) current = Path.Combine(current, pieces[i]);
                    var parent = Open(current, 0x80, 0x02200000);
                    parents.Add(parent);
                    var info = Info(parent);
                    if ((info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0) throw new IOException("ReparseAncestorRejected");
                }
                var handle = Open(full, LocalFileAccess, LocalFileFlags);
                try
                {
                    if ((Info(handle).Attributes & 0x410) != 0 || GetFileType(handle) != 1) throw new IOException("ReparseOrNonDiskFileRejected");
                    var final = new StringBuilder(32768);
                    uint count = GetFinalPathNameByHandle(handle, final, (uint)final.Capacity, 0);
                    if (count == 0 || count >= final.Capacity ||
                        !string.Equals(final.ToString(), @"\\?\" + full, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("ResolvedPathMismatch");
                    stream = new FileStream(handle, FileAccess.Read, 65536, false);
                }
                catch { handle.Dispose(); throw; }
            }
            catch { Dispose(); throw; }
        }
        public Identity Snapshot() => Info(stream!.SafeFileHandle);
        public void Dispose() { stream?.Dispose(); for (int i = parents.Count - 1; i >= 0; i--) parents[i].Dispose(); }
        private static SafeFileHandle Open(string path, uint access, uint flags)
        {
            var h = CreateFile(path, access, LocalShareMode /* FileShare.Read only */, IntPtr.Zero, 3, flags, IntPtr.Zero);
            if (h.IsInvalid) { int error = Marshal.GetLastWin32Error(); h.Dispose(); throw new Win32Exception(error); }
            return h;
        }
        private static Identity Info(SafeFileHandle h)
        {
            if (!GetFileInformationByHandle(h, out var i)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetFileInformationByHandleEx(h, 18 /* FileIdInfo */, out var id, 24))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(id.Volume, id.Low, checked((long)(((ulong)i.SizeHigh << 32) | i.SizeLow)),
                ((long)i.CreatedHigh << 32) | i.CreatedLow, ((long)i.WriteHigh << 32) | i.WriteLow, i.Attributes, id.High);
        }
        [StructLayout(LayoutKind.Sequential)] private struct FileIdNative { public ulong Volume, Low, High; }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int kind, out FileIdNative id, uint size);
        [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
        { public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string p, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle h, out FileInfoNative info);
        [DllImport("kernel32.dll")] private static extern uint GetFileType(SafeFileHandle h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder b, uint n, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryDosDeviceW")]
        private static extern uint QueryDosDevice(string device, StringBuilder target, int max);
    }
}
