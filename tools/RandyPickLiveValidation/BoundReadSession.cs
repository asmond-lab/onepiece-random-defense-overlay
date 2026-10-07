using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using OrandOverlay;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("OrandOverlay.Tests")]

internal sealed class BoundReadSession : IDisposable
{
    internal const int CopyLength = 52885712;
    internal enum QueryClass { AddressWalk, ReadValidation }
    internal sealed record SizeClassMetrics(long Requests, long RequestedBytes, long ReturnedBytes);
    internal sealed record QueryMetrics(long Calls, double Milliseconds, double MaximumMilliseconds, long Failures, int LastOsError);
    internal sealed record DiscoveryMetricsSnapshot(long ReadCalls, long RequestedBytes, long ReturnedBytes,
        SizeClassMetrics LargeSizeClassAtLeast4096, SizeClassMetrics SmallSizeClassBelow4096,
        QueryMetrics AddressWalkVq, QueryMetrics ReadValidationVq,
        long RpmCalls, long RpmRequestedBytes, long RpmReturnedBytes, double RpmMilliseconds, double RpmMaximumMilliseconds,
        long RpmFailedCalls, long RpmPartialCalls, int LastRpmOsError, long YieldedRegions, long YieldedRegionBytes,
        double DiscoveryElapsedMilliseconds, double NativeCallMilliseconds, double ElapsedMinusNativeCallsMilliseconds);
    // Fixed-size, aggregate-only counters. No address, name, buffer, exception text or per-call history is retained.
    internal sealed class DiscoveryMetrics
    {
        private const double MaximumMilliseconds = 1e12;
        private readonly Func<long> timestamp;
        private readonly long frequency, started;
        private long? ended;
        private long requests, requested, returned, largeRequests, largeRequested, largeReturned,
            smallRequests, smallRequested, smallReturned, walkCalls, walkFailures, validationCalls, validationFailures,
            rpmCalls, rpmRequested, rpmReturned, rpmFailures, rpmPartials, regions, regionBytes;
        private double walkMs, walkMax, validationMs, validationMax, rpmMs, rpmMax;
        private int walkError, validationError, rpmError;
        internal DiscoveryMetrics(Func<long>? timestamp = null, long? frequency = null)
        {
            this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
            this.frequency = Math.Max(1, frequency ?? Stopwatch.Frequency);
            started = this.timestamp();
        }
        private static long Add(long value, long increment) => value + Math.Min(Math.Max(0, increment), long.MaxValue - value);
        private static double Bounded(double value) => Math.Clamp(double.IsFinite(value) ? value : MaximumMilliseconds, 0, MaximumMilliseconds);
        private double Milliseconds(long first, long last) => Bounded(((double)last - first) * 1000.0 / frequency);
        internal void Request(int count)
        {
            if (ended.HasValue || count <= 0) return;
            requests = Add(requests, 1); requested = Add(requested, count);
            if (count >= 4096) { largeRequests = Add(largeRequests, 1); largeRequested = Add(largeRequested, count); }
            else { smallRequests = Add(smallRequests, 1); smallRequested = Add(smallRequested, count); }
        }
        internal void Query(QueryClass kind, bool success, int osError, long first, long last)
        {
            if (ended.HasValue) return;
            var ms = Milliseconds(first, last);
            if (kind == QueryClass.AddressWalk)
            {
                walkCalls = Add(walkCalls, 1); walkMs = Bounded(walkMs + ms); walkMax = Math.Max(walkMax, ms);
                if (!success) { walkFailures = Add(walkFailures, 1); walkError = Math.Max(0, osError); }
            }
            else
            {
                validationCalls = Add(validationCalls, 1); validationMs = Bounded(validationMs + ms); validationMax = Math.Max(validationMax, ms);
                if (!success) { validationFailures = Add(validationFailures, 1); validationError = Math.Max(0, osError); }
            }
        }
        internal void Rpm(int count, long obtained, bool success, int osError, long first, long last)
        {
            if (ended.HasValue) return;
            var n = Math.Max(0, count); var got = Math.Clamp(obtained, 0, n); var ms = Milliseconds(first, last);
            rpmCalls = Add(rpmCalls, 1); rpmRequested = Add(rpmRequested, n); rpmReturned = Add(rpmReturned, got);
            returned = Add(returned, got); rpmMs = Bounded(rpmMs + ms); rpmMax = Math.Max(rpmMax, ms);
            if (!success) { rpmFailures = Add(rpmFailures, 1); rpmError = Math.Max(0, osError); }
            if (obtained != count) rpmPartials = Add(rpmPartials, 1);
            if (count >= 4096) largeReturned = Add(largeReturned, got); else smallReturned = Add(smallReturned, got);
        }
        internal void Yield(ulong size)
        {
            if (ended.HasValue) return;
            regions = Add(regions, 1); regionBytes = Add(regionBytes, (long)Math.Min(size, (ulong)long.MaxValue));
        }
        internal DiscoveryMetricsSnapshot Complete()
        {
            ended ??= timestamp();
            return Snapshot();
        }
        internal DiscoveryMetricsSnapshot Snapshot()
        {
            var elapsed = Milliseconds(started, ended ?? timestamp());
            var native = Bounded(walkMs + validationMs + rpmMs);
            return new(requests, requested, returned, new(largeRequests, largeRequested, largeReturned),
                new(smallRequests, smallRequested, smallReturned), new(walkCalls, walkMs, walkMax, walkFailures, walkError),
                new(validationCalls, validationMs, validationMax, validationFailures, validationError),
                rpmCalls, rpmRequested, rpmReturned, rpmMs, rpmMax, rpmFailures, rpmPartials, rpmError,
                regions, regionBytes, elapsed, native, Bounded(elapsed - native));
        }
    }
    private DiscoveryMetrics? discoveryMetrics;
    internal void BeginDiscoveryMetrics() => discoveryMetrics = new DiscoveryMetrics();
    internal DiscoveryMetricsSnapshot? CompleteDiscoveryMetrics()
    {
        var metrics = discoveryMetrics; discoveryMetrics = null;
        return metrics?.Complete(); // Counter/clock access only; no query/read, no probe budget reset.
    }

    internal sealed record Identity(int Pid, long Start, ulong Module, int Size, string ImagePath);
    private readonly SafeProcessHandle handle;
    private readonly ExpectedReadTarget target;
    private readonly Identity identity;
    private readonly CancellationToken token;
    private readonly byte[] header, resource;
    private readonly ulong resourceAddress;
    private ulong peb;
    private byte[]? pebFields;
    private bool disposed;
    internal sealed record BudgetSnapshot(long RequestedReadBytes, int ReadCalls, double ElapsedMilliseconds,
        long ByteLimit = 2 * 1024 * 1024, double DeadlineMilliseconds = 5000);
    // One shared budget owns binding, payload/replay AND closing world reads. Never restarted inside a probe.
    internal sealed class ProbeBudget
    {
        internal const long MaximumBytes = 2 * 1024 * 1024;
        private readonly Func<TimeSpan> elapsed;
        private long bytes; private int calls;
        internal ProbeBudget(Func<TimeSpan> elapsed) => this.elapsed = elapsed;
        internal void Check()
        {
            var age = elapsed();
            Need(age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(5), "Extra probe five-second cap");
        }
        internal void Charge(int count)
        {
            Check(); Need(count > 0 && count <= MaximumBytes - bytes, "Extra probe byte cap");
            bytes += count; calls++;
        }
        // Failure reports must still be able to snapshot an expired budget without throwing again.
        internal BudgetSnapshot Snapshot() => new(bytes, calls, elapsed().TotalMilliseconds);
    }
    private ProbeBudget? probeBudget;
    internal void BeginSampleBudget() { probeBudget = null; }
    internal void BeginProbeBudget()
    {
        Need(probeBudget is null, "Probe budget already started");
        var watch = Stopwatch.StartNew(); probeBudget = new ProbeBudget(() => watch.Elapsed);
    }
    internal BudgetSnapshot? SharedProbeBudget => probeBudget?.Snapshot();
    internal void CheckProbeDeadline() { token.ThrowIfCancellationRequested(); probeBudget?.Check(); }
    private void BudgetCheck() => CheckProbeDeadline();
    internal ulong Module => identity.Module;
    internal string SessionKey => $"copy-bound:{identity.Pid}:{identity.Start}:{identity.Module:X}:{Warcraft300Diagnostic.Hash}";
    private BoundReadSession(SafeProcessHandle h, ExpectedReadTarget t, Identity i, byte[] image, CancellationToken ct)
    {
        handle = h; target = t; identity = i; token = ct;
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var ph = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("Missing PE header");
        Need(pe.PEHeaders.CoffHeader.Machine == Machine.Amd64 && ph.Magic == PEMagic.PE32Plus && ph.SizeOfImage == i.Size, "PE architecture/image size mismatch");
        header = image.AsSpan(0, ph.SizeOfHeaders).ToArray();
        int optional = checked(BitConverter.ToInt32(image, 60) + 24);
        // Observed loader normalization: only PE32+ ImageBase is replaced, no other byte exclusions.
        BitConverter.GetBytes(i.Module).CopyTo(header, optional + 24);
        var rs = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".rsrc");
        Need(rs.SizeOfRawData > 0 && rs.VirtualAddress >= 0 && (long)rs.VirtualAddress + rs.SizeOfRawData <= i.Size, "Resource bounds");
        resource = image.AsSpan(rs.PointerToRawData, rs.SizeOfRawData).ToArray();
        resourceAddress = checked(i.Module + (ulong)rs.VirtualAddress);
        bool version = false;
        for (int p = 0; p + 16 <= resource.Length; p += 4)
            if (BitConverter.ToUInt32(resource, p) == 0xFEEF04BD && BitConverter.ToUInt32(resource, p + 8) == 0x00030000 &&
                BitConverter.ToUInt32(resource, p + 12) == 24268) version = true;
        Need(version, "Pinned copy fixed-file version 3.0.0.24268 absent");
    }
    internal static Identity Metadata(ExpectedReadTarget target)
    {
        var processes = Process.GetProcessesByName("Warcraft III").Concat(Process.GetProcessesByName("WarcraftIII")).ToArray();
        try
        {
            Need(processes.Length == 1, "Nonunique target"); var p = processes[0];
            Need(!p.HasExited, "Target exited"); target.EnsureMatches(p.Id, p.StartTime.ToUniversalTime().Ticks);
            var m = p.MainModule ?? throw new InvalidDataException("Module unavailable");
            Need(m.ModuleName == "Warcraft III.exe", "Main module name mismatch");
            // Module filename is OS metadata only. Never open/stat/version-query this installed path.
            return new(p.Id, p.StartTime.ToUniversalTime().Ticks, (ulong)m.BaseAddress.ToInt64(), m.ModuleMemorySize, m.FileName);
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }
    internal static T Acquire<T>(Action guard, Func<byte[]> readCopy, Func<byte[], T> open, Action<T> bind) where T : IDisposable
    {
        guard(); var copy = readCopy(); guard(); // No copy I/O or native open before exact identity.
        var lease = open(copy);
        try { bind(lease); return lease; } catch { lease.Dispose(); throw; }
    }
    internal static BoundReadSession Open(ExpectedReadTarget target, string copyPath, CancellationToken token)
    {
        Identity? first = null;
        void Guard() { token.ThrowIfCancellationRequested(); var now = Metadata(target); first ??= now; Need(first == now, "Module/epoch changed"); }
        return Acquire(Guard, () => ReadCopy(copyPath, first!.ImagePath), image =>
        {
            // Acquire just repeated exact PID/start/module metadata immediately before this 0x410 open.
            var h = OpenProcess(0x410, false, target.ProcessId);
            try { Need(!h.IsInvalid, "Read/query handle denied"); return new BoundReadSession(h, target, first!, image, token); }
            catch { h.Dispose(); throw; }
        }, lease => lease.Revalidate());
    }
    internal static string ValidateCopyPath(string supplied, string installed)
    {
        Need(Path.IsPathFullyQualified(supplied) && supplied.Length > 3 && char.IsAsciiLetter(supplied[0]) && supplied[1] == ':' &&
            supplied[2] == (char)92 && !supplied.AsSpan(2).Contains(':') && !supplied.Contains('/') && !supplied.Split((char)92).Any(x => x.EndsWith(' ') || x.EndsWith('.')), "Local regular copy path required");
        Need(!supplied.Contains('~') && !supplied.Contains('?') && !supplied.Contains('*'), "Short-name/device/wildcard aliases forbidden");
        foreach (var component in supplied.Split((char)92).Skip(1))
        {
            Need(!component.Equals("MemoryDiagnostics", StringComparison.OrdinalIgnoreCase), "Excluded copy path");
            var stem = component.Split('.')[0].ToUpperInvariant();
            Need(stem is not ("CON" or "PRN" or "AUX" or "NUL") &&
                !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] >= '1' && stem[3] <= '9'), "Device name forbidden");
        }
        var full = Path.GetFullPath(supplied);
        Need(!string.Equals(full, Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase), "Installed image path forbidden");
        return full;
    }
    private static byte[] ReadCopy(string supplied, string installed)
    {
        var full = ValidateCopyPath(supplied, installed);
        for (FileSystemInfo? f = new FileInfo(full); f is not null; f = f is FileInfo file ? file.Directory : ((DirectoryInfo)f).Parent)
            Need((f.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse copy/ancestor forbidden");
        Need((File.GetAttributes(full) & (FileAttributes.Directory | FileAttributes.Device)) == 0, "Not regular copy");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        Need(GetFileInformationByHandle(stream.SafeFileHandle, out var info) && info.Links == 1 && (info.Attributes & 0x410) == 0, "Nonregular/link copy rejected");
        var final = new System.Text.StringBuilder(32768);
        Need(GetFinalPathNameByHandle(stream.SafeFileHandle, final, final.Capacity, 0) is > 0 and < 32768, "Copy final path unavailable");
        var finalPath = final.ToString();
        Need(finalPath.StartsWith(new string((char)92, 2) + "?" + (char)92) && string.Equals(ValidateCopyPath(finalPath[4..], installed), full, StringComparison.OrdinalIgnoreCase), "Copy path changed");
        Need(stream.Length == CopyLength, "Exact copy length mismatch");
        var bytes = new byte[CopyLength]; stream.ReadExactly(bytes); Need(stream.ReadByte() == -1, "Copy changed");
        Need(Convert.ToHexString(SHA256.HashData(bytes)) == Warcraft300Diagnostic.Hash, "Exact authorized copy hash mismatch");
        // Hash pins the copy's version; version resources are then compared in full in the live image.
        return bytes;
    }
    internal static void EqualBytes(byte[] expected, byte[] actual) => Need(expected.AsSpan().SequenceEqual(actual), "Runtime/copy or bracket bytes mismatch");
    internal static byte[] ExactRead(ulong address, int count,
        Func<ulong, ReadOnlyProcessMemory.ModuleRegionInfo?> query, Func<ulong, int, byte[]> read, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Need(count > 0 && address >= 0x10000 && address <= 0x7FFFFFFFFFFF && (ulong)(count - 1) <= 0x7FFFFFFFFFFF - address, "Read range");
        ulong cursor = address;
        foreach (var span in ReadOnlyProcessMemory.EnumerateReadableModuleRegions(address, count, query, token))
        { Need(span.BaseAddress == cursor, "Unreadable span"); cursor = checked(cursor + span.Size); }
        Need(cursor == address + (ulong)count, "Unreadable span"); token.ThrowIfCancellationRequested();
        var bytes = read(address, count); token.ThrowIfCancellationRequested(); Need(bytes.Length == count, "Partial native read"); return bytes;
    }
    private ReadOnlyProcessMemory.ModuleRegionInfo? QueryWalk(ulong a) => QueryNative(a, QueryClass.AddressWalk);
    private ReadOnlyProcessMemory.ModuleRegionInfo? QueryRead(ulong a) => QueryNative(a, QueryClass.ReadValidation);
    private ReadOnlyProcessMemory.ModuleRegionInfo? QueryNative(ulong a, QueryClass kind)
    {
        BudgetCheck(); ObjectDisposedException.ThrowIf(disposed, this);
        var length = (nuint)Marshal.SizeOf<Mbi>();
        var metrics = discoveryMetrics; var first = metrics is null ? 0 : Stopwatch.GetTimestamp();
        var queried = VirtualQueryEx(handle, (nint)a, out var m, length);
        int error = queried == length ? 0 : Marshal.GetLastWin32Error();
        if (metrics is not null) metrics.Query(kind, queried == length, error, first, Stopwatch.GetTimestamp());
        if (queried != length) return null;
        return new(m.Base, m.Size, m.State, m.Protect, m.Type);
    }
    internal byte[] Read(ulong a, int n)
    {
        BudgetCheck(); probeBudget?.Charge(n);
        discoveryMetrics?.Request(n);
        var result = ExactRead(a, n, QueryRead, (address, count) =>
        {
            var bytes = new byte[count];
            var metrics = discoveryMetrics; var first = metrics is null ? 0 : Stopwatch.GetTimestamp();
            var ok = ReadProcessMemory(handle, (nint)address, bytes, count, out var got);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            if (metrics is not null) metrics.Rpm(count, got.ToInt64(), ok, error, first, Stopwatch.GetTimestamp());
            Need(ok && got == count, "Failed/partial native read"); return bytes;
        }, token);
        BudgetCheck(); return result; // A native read that returns at/after the deadline must fail.
    }
    internal IEnumerable<MemoryRegion> Regions()
    {
        foreach (var region in ReadOnlyPrivateRegionScan.Enumerate(QueryWalk, token))
        {
            discoveryMetrics?.Yield(region.Size);
            yield return region; // Streaming only: no region arrays or address history retained.
        }
    }
    private ulong Peb()
    {
        var pbi = new byte[48]; var wow = new byte[8];
        Need(NtQueryInformationProcess(handle, 0, pbi, 48, out var count) >= 0 && count == 48 && BitConverter.ToUInt64(pbi, 32) == (ulong)target.ProcessId, "PBI identity mismatch");
        Need(NtQueryInformationProcess(handle, 26, wow, 8, out count) >= 0 && count == 8 && BitConverter.ToUInt64(wow) == 0, "Native AMD64 required");
        return BitConverter.ToUInt64(pbi, 8);
    }
    internal void Revalidate()
    {
        token.ThrowIfCancellationRequested(); Need(Metadata(target) == identity, "Target/module changed");
        var currentPeb = Peb(); Need(peb == 0 || peb == currentPeb, "PEB changed"); peb = currentPeb;
        var fields = Read(checked(peb + 0x10), 16); Need(BitConverter.ToUInt64(fields) == Module, "PEB main image mismatch");
        if (pebFields is not null) EqualBytes(pebFields, fields); else pebFields = fields;
        EqualBytes(header, Read(Module, header.Length)); EqualBytes(resource, Read(resourceAddress, resource.Length));
        Need(Metadata(target) == identity, "Post-binding target changed"); token.ThrowIfCancellationRequested();
        BudgetCheck(); // Includes the final PE/resource read AND final metadata call, not just read entry.
    }
    public void Dispose() { if (!disposed) { disposed = true; handle.Dispose(); } }
    private static void Need(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct Mbi { public ulong Base, Allocation; public uint AllocationProtect; public ushort Partition; public ulong Size; public uint State, Protect, Type; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle h, nint a, byte[] b, int n, out nint got);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern nuint VirtualQueryEx(SafeProcessHandle h, nint a, out Mbi m, nuint n);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle h, int kind, byte[] b, uint n, out uint got);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle h, out FileInfoNative info);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle h, System.Text.StringBuilder path, int size, uint flags);
}
