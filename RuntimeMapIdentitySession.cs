using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OrandOverlay;

// One recognizer's process-session proof. The archive and its ancestors remain read-only
// locked until invalidation; path reopens compare actual Windows file IDs, not timestamps.
public sealed class RuntimeMapIdentitySession : IDisposable
{
    private readonly object gate = new();
    private readonly Action<bool, int>? observedLogRead;
    public RuntimeMapIdentitySession(Action<bool, int>? observedLogRead = null) =>
        this.observedLogRead = observedLogRead;
    internal void ObserveOuterRead(int bytes) => observedLogRead?.Invoke(false, bytes);
    private NativeMapArchiveBinding.IArchive? archive;
    private FileStream? logHandle;
    private NativeMapArchiveBinding.Identity? identity;
    private (uint Volume, uint High, uint Low)? logId;
    private (int Pid, long Ticks, DateTimeOffset Start, string LogPath, DateTime LogCreation,
        long LogLength, string LogTailHash, string Path, DateTimeOffset Opening,
        string PinName, long PinLength, string PinHash)? key;

    public void Reset() { lock (gate) Clear(); }
    public void ResetIfDifferent(int pid, long ticks)
    {
        lock (gate)
            if (key is { } active && (active.Pid != pid || active.Ticks != ticks)) Clear();
    }
    public void Dispose() => Reset();
    private void Clear()
    {
        archive?.Dispose(); archive = null;
        logHandle?.Dispose(); logHandle = null;
        identity = null; key = null; logId = null;
    }

    internal RuntimeMapIdentityResult Verify(int pid, long ticks, DateTimeOffset start,
        string logPath, DateTime logCreation, long logLength, string logTailHash,
        string path, DateTimeOffset opening, MapArchivePin pin, long logBytes, int logCalls)
    {
        lock (gate)
        {
            var next = (pid, ticks, start, Path.GetFullPath(logPath), logCreation,
                logLength, logTailHash, path, opening, pin.FileName, pin.LengthBytes, pin.Sha256);
            var totalLogBytes = logBytes;
            var totalLogCalls = logCalls;
            string ReadHeldTail()
            {
                var (hash, bytes) = TailHash(logHandle!);
                totalLogBytes += bytes;
                totalLogCalls++;
                return hash;
            }
            try
            {
                if (archive is not null && key == next && logHandle is { SafeFileHandle.IsInvalid: false } &&
                    logHandle.Length == logLength && ReadHeldTail() == logTailHash &&
                    identity == archive.Snapshot())
                {
                    using var currentLog = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                    if (logId == GetLogId(currentLog.SafeFileHandle) &&
                        logId == GetLogId(logHandle.SafeFileHandle))
                    {
                    using var reopened = NativeMapArchiveBinding.OpenLocalArchive(path);
                    if (reopened.Snapshot() == identity && archive.Snapshot() == identity)
                        return Result(RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None,
                            "Session-bound archive proof reused under read-only Windows handles.", 0, 0);
                    }
                }
                Clear();
                if (!OperatingSystem.IsWindows())
                    return Result(RuntimeMapIdentityState.Unknown, RuntimeMapIdentityFailure.ArchiveMissing,
                        "Windows file identity and mutation exclusion are required.", 0, 0);
                logHandle = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite);
                var info = new FileInfo(logPath);
                if (info.CreationTimeUtc != logCreation || info.Length != logLength ||
                    logHandle.Length != logLength)
                    return Fail("Log continuity changed while locking the session.");
                logId = GetLogId(logHandle.SafeFileHandle);
                if (ReadHeldTail() != logTailHash)
                    return Fail("Log tail changed before hashing.");
                using (var currentLog = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite))
                    if (GetLogId(currentLog.SafeFileHandle) != logId)
                        return Fail("Log path changed before hashing.");
                archive = NativeMapArchiveBinding.OpenLocalArchive(path);
                var before = archive.Snapshot();
                if (before.Length != pin.LengthBytes)
                    return ResultAndClear(RuntimeMapIdentityFailure.ArchiveLengthMismatch,
                        "Archive length differs from expected pin.", 0, 0);
                var hash = Convert.ToHexString(SHA256.HashData(archive.Content)).ToLowerInvariant();
                var after = archive.Snapshot();
                if (before != after || logHandle.Length != logLength ||
                    ReadHeldTail() != logTailHash ||
                    new FileInfo(logPath).Length != logLength)
                    return Fail("Log or archive changed during hashing.", pin.LengthBytes, 1);
                if (!hash.Equals(pin.Sha256, StringComparison.OrdinalIgnoreCase))
                    return ResultAndClear(RuntimeMapIdentityFailure.ArchiveHashMismatch,
                        "Archive SHA-256 differs from expected pin.", pin.LengthBytes, 1, hash);
                using var current = NativeMapArchiveBinding.OpenLocalArchive(path);
                if (current.Snapshot() != after)
                    return Fail("Archive path changed during hashing.", pin.LengthBytes, 1);
                identity = after;
                key = next;
                return Result(RuntimeMapIdentityState.Proven, RuntimeMapIdentityFailure.None,
                    "Pinned archive hashed with a retained read-only Windows handle.", pin.LengthBytes, 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or System.Security.SecurityException)
            {
                return Fail("Archive or log cannot be locked and checked: " + ex.GetType().Name);
            }

            RuntimeMapIdentityResult Fail(string reason, long bytes = 0, int calls = 0) =>
                ResultAndClear(RuntimeMapIdentityFailure.LogReadFailed, reason, bytes, calls);
            RuntimeMapIdentityResult ResultAndClear(RuntimeMapIdentityFailure failure, string reason,
                long bytes, int calls, string hash = "")
            {
                Clear();
                return Result(RuntimeMapIdentityState.Unknown, failure, reason, bytes, calls, hash);
            }
            RuntimeMapIdentityResult Result(RuntimeMapIdentityState state, RuntimeMapIdentityFailure failure,
                string reason, long bytes, int calls, string? hash = null) =>
                new(state, failure, reason, opening, RuntimeMapIdentityProvider.PathField,
                    path, pin.LengthBytes, hash ?? (state == RuntimeMapIdentityState.Proven ? pin.Sha256 : ""),
                    totalLogBytes, totalLogCalls, bytes, calls);
        }
    }

    private (string Hash, int Bytes) TailHash(FileStream stream)
    {
        var count = (int)Math.Min(stream.Length, RuntimeMapIdentityProvider.MaximumLogTailBytes);
        stream.Position = stream.Length - count;
        var bytes = new byte[count];
        stream.ReadExactly(bytes);
        observedLogRead?.Invoke(true, count);
        return (Convert.ToHexString(SHA256.HashData(bytes)), count);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoNative
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh,
            WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
    private static (uint Volume, uint High, uint Low) GetLogId(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return (info.Volume, info.IndexHigh, info.IndexLow);
    }
}
