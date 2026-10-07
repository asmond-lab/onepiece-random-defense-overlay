using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WarcraftProbe;

/// <summary>Read/query-only main-image capture. Never executes or changes target memory.</summary>
public static class WindowsCollector
{
    internal const long MaximumReadBytes = 8 * 1024 * 1024;
    internal const int MaximumQueries = 50000;
    internal sealed record Identity(int Pid, DateTimeOffset Start, ulong Module, uint Size, string ImagePath);
    internal sealed record MemoryRegion(ulong Base, ulong Size, uint State, uint Protection, uint Type);
    internal sealed class UnreadableException : IOException { internal UnreadableException() : base("Whole range is not committed readable non-guard memory") { } }
    internal sealed class Budget
    {
        private readonly Func<TimeSpan> elapsed;
        private readonly CancellationToken token;
        internal long Bytes { get; private set; }
        internal int Queries { get; private set; }
        internal double Milliseconds => elapsed().TotalMilliseconds;
        internal Budget(Func<TimeSpan> elapsed, CancellationToken token = default) { this.elapsed = elapsed; this.token = token; }
        internal void Check() { token.ThrowIfCancellationRequested(); var t = elapsed(); Need(t >= TimeSpan.Zero && t < TimeSpan.FromSeconds(10), "Capture deadline reached"); }
        internal void Charge(int count) { Check(); Need(count > 0 && count <= MaximumReadBytes - Bytes, "Native read budget exhausted"); Bytes += count; }
        internal void Query() { Check(); Need(Queries < MaximumQueries, "Query budget exhausted"); Queries++; }
    }
    internal static void Need(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
    internal static void Range(ulong a, int n) => Need(n > 0 && a >= 0x10000 && a <= 0x7FFFFFFFFFFF && (ulong)(n - 1) <= 0x7FFFFFFFFFFF - a, "Invalid read span");
    internal static bool Readable(MemoryRegion r) => r.State == 0x1000 && (r.Protection & (0x100 | 0x01)) == 0 &&
        (r.Protection & 0xff) is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    internal static void CheckReadable(ulong address, int count, Func<ulong, MemoryRegion?> query, Action check)
    {
        Range(address, count); var end = checked(address + (ulong)count); var cursor = address;
        while (cursor < end)
        {
            check(); var r = query(cursor); check();
            if (r is null || r.Size == 0 || r.Base > cursor || r.Base > ulong.MaxValue - r.Size || r.Base + r.Size <= cursor || !Readable(r)) throw new UnreadableException();
            cursor = Math.Min(end, r.Base + r.Size);
        }
        check();
    }
    // Injection seam preserves preflight of the ENTIRE range before the single RPM.
    internal static byte[] ReadExact(ulong address, int count, Func<ulong, MemoryRegion?> query,
        Func<ulong, int, byte[]> rpm, Budget budget, CancellationToken token = default)
    {
        void Check() { token.ThrowIfCancellationRequested(); budget.Check(); }
        CheckReadable(address, count, query, Check); Check(); budget.Charge(count);
        var bytes = rpm(address, count); Check(); Need(bytes is not null && bytes.Length == count, "Failed or partial native read"); return bytes!;
    }
    internal static Identity Pin(int? expectedPid, DateTimeOffset? expectedStart, Func<Identity[]> discover)
    {
        Need(expectedPid.HasValue == expectedStart.HasValue, "PID and start must both be supplied or both omitted");
        Need(!expectedPid.HasValue || expectedPid.Value > 0, "Invalid expected PID");
        var all = discover();
        var candidates = expectedPid.HasValue ? all.Where(i => i.Pid == expectedPid.Value).ToArray() : all;
        Need(candidates.Length == 1, "Target missing or ambiguous"); var selected = candidates[0];
        Need(!expectedPid.HasValue || selected.Start == expectedStart, "Process epoch mismatch"); return selected;
    }
    internal static T IdentityBeforeCopy<T>(Func<Identity> pin, Func<Identity, T> readCopy, out Identity identity)
    { identity = pin(); return readCopy(identity); }
    internal static T AfterEpoch<T>(int pid, DateTimeOffset start, int? expectedPid, DateTimeOffset? expectedStart, Func<T> moduleMetadata)
    {
        Need(expectedPid.HasValue == expectedStart.HasValue, "PID/start pair required before module metadata");
        Need(!expectedPid.HasValue || (pid == expectedPid.Value && start == expectedStart!.Value), "Process epoch mismatch before module metadata");
        return moduleMetadata();
    }
    private static Identity[] Discover(int? pid, DateTimeOffset? expectedStart = null)
    {
        Process[] processes = pid.HasValue ? new[] { Process.GetProcessById(pid.Value) } :
            Process.GetProcessesByName("Warcraft III").Concat(Process.GetProcessesByName("WarcraftIII")).ToArray();
        try
        {
            return processes.Select(p =>
            {
                Need(!p.HasExited, "Target exited");
                // OS metadata only. The installed image path is never opened, statted or version queried.
                var start = new DateTimeOffset(p.StartTime.ToUniversalTime());
                Need(p.ProcessName.Equals("Warcraft III", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("WarcraftIII", StringComparison.OrdinalIgnoreCase), "Not a Warcraft process");
                return AfterEpoch(p.Id, start, pid, expectedStart, () =>
                {
                    var m = p.MainModule ?? throw new InvalidDataException("Main module unavailable");
                    Need(m.ModuleMemorySize > 0, "Invalid module size");
                    return new Identity(p.Id, start, unchecked((ulong)m.BaseAddress.ToInt64()), (uint)m.ModuleMemorySize, m.FileName);
                });
            }).ToArray();
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }
    internal static string ValidateCopyPath(string supplied, string installed)
    {
        Need(!string.IsNullOrEmpty(supplied) && supplied.Length > 3 && char.IsAsciiLetter(supplied[0]) && supplied[1] == ':' && supplied[2] == '\\', "Local absolute regular copy required");
        Need(!supplied.AsSpan(2).Contains(':') && !supplied.Contains('/') && !supplied.Contains('~') && !supplied.Contains('?') && !supplied.Contains('*'), "Network/device/ADS/alias paths forbidden");
        foreach (var c in supplied.Split('\\').Skip(1))
        {
            Need(!c.Equals("MemoryDiagnostics", StringComparison.OrdinalIgnoreCase), "Excluded copy path");
            Need(c.Length > 0 && !c.EndsWith(' ') && !c.EndsWith('.') && !c.Any(char.IsControl), "Noncanonical copy component");
            var stem = c.Split('.')[0].ToUpperInvariant();
            Need(stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$") && !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && (char.IsDigit(stem[3]) || "¹²³".Contains(stem[3]))), "Device component forbidden");
        }
        var full = Path.GetFullPath(supplied); Need(string.Equals(full, supplied, StringComparison.OrdinalIgnoreCase), "Path aliases forbidden");
        var osPath = installed.StartsWith(@"\\?\", StringComparison.Ordinal) ? installed[4..] : installed;
        Need(!string.Equals(full, Path.GetFullPath(osPath), StringComparison.OrdinalIgnoreCase), "Installed image path forbidden"); return full;
    }
    private static void CheckAncestors(string full)
    {
        for (FileSystemInfo? f = new FileInfo(full); f is not null; f = f is FileInfo file ? file.Directory : ((DirectoryInfo)f).Parent)
            Need((f.Attributes & FileAttributes.ReparsePoint) == 0, "Reparse copy or ancestor forbidden");
    }
    private static byte[] ReadCopy(string supplied, string installed, Budget budget)
    {
        budget.Check(); var full = ValidateCopyPath(supplied, installed);
        Need(GetDriveType(Path.GetPathRoot(full)!) is 2 or 3 or 6, "Network or unknown drive forbidden"); CheckAncestors(full);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        FileInfoNative Inspect()
        {
            Need(GetFileInformationByHandle(stream.SafeFileHandle, out var info) && info.Links == 1 && (info.Attributes & 0x450) == 0, "Nonregular/reparse/hardlinked copy forbidden");
            var final = new StringBuilder(32768); var n = GetFinalPathNameByHandle(stream.SafeFileHandle, final, final.Capacity, 0);
            Need(n > 0 && n < final.Capacity, "Copy final path unavailable"); var resolved = final.ToString();
            Need(resolved.StartsWith(@"\\?\", StringComparison.Ordinal) && string.Equals(ValidateCopyPath(resolved[4..], installed), full, StringComparison.OrdinalIgnoreCase), "Final copy path alias forbidden");
            return info;
        }
        var before = Inspect(); Need(stream.Length > 0 && stream.Length <= 256 * 1024 * 1024, "Copy file size outside bounds");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); Need(stream.ReadByte() == -1, "Copy size changed"); budget.Check();
        Need(before.Equals(Inspect()), "Copy identity changed"); CheckAncestors(full); budget.Check(); return bytes;
    }
    internal static RegionInfo[] EnumerateRegions(ulong module, uint size, Func<ulong, MemoryRegion?> query, Action check)
    {
        Need(size > 0 && module >= 0x10000 && module <= 0x7FFFFFFFFFFF && size - 1UL <= 0x7FFFFFFFFFFF - module, "Image extent invalid");
        var result = new List<RegionInfo>(); var cursor = module; var end = checked(module + size);
        while (cursor < end)
        {
            check(); var r = query(cursor); check();
            Need(r is not null && r.Base <= cursor && r.Size > 0 && r.Base <= ulong.MaxValue - r.Size && r.Base + r.Size > cursor, "Incomplete main-image region map");
            var next = Math.Min(end, r!.Base + r.Size); var item = new RegionInfo(checked((uint)(cursor - module)), next - cursor, r.State, r.Protection, r.Type);
            if (result.Count > 0 && result[^1].State == item.State && result[^1].Protection == item.Protection && result[^1].Type == item.Type)
                result[^1] = result[^1] with { Size = result[^1].Size + item.Size };
            else result.Add(item);
            cursor = next;
        }
        check(); return result.ToArray();
    }
    internal static readonly (string Name, uint Rva, uint Size)[] KnownFunctions =
    {
        ("GetWidgetLife",0xCAFBE0,0x31),("GetUnitState",0xCAF3B0,0x40),("IsUnitType",0xCB6690,0x2A4),
        ("IsUnitHidden",0xCB5510,0x1E),("GetOwningPlayer",0xCA8970,0x29),("GetLocalPlayer",0xCA73D0,0x10D),
        ("GetPlayerId",0xCA8B50,0x49),("BlzFrameGetText",0xC9EF10,0x1F1),("BlzFrameSetText",0xCA0160,0x24)
    };
    internal static CodeCheck[] CheckCode(ImageInfo image, ulong module, Func<ulong, int, byte[]> read)
    {
        if (!Known300Adapter.Allows(image)) return Array.Empty<CodeCheck>();
        return KnownFunctions.Select(f =>
        {
            Need((ulong)f.Rva + f.Size <= image.ImageSize, "Function outside image");
            try { var bytes = read(checked(module + f.Rva), checked((int)f.Size)); Need(bytes.Length == f.Size, "Partial function read"); return new CodeCheck(f.Name, f.Rva, f.Size, "CodeCheckHashed", Convert.ToHexString(SHA256.HashData(bytes))); }
            catch (UnreadableException) { return new CodeCheck(f.Name, f.Rva, f.Size, "CodeCheckBlocked", null); }
        }).ToArray();
    }
    public static Snapshot Capture(string copyPath, string label, int? expectedPid, DateTimeOffset? expectedStart, bool structures, CancellationToken cancellationToken)
    {
        Need(OperatingSystem.IsWindows() && Environment.Is64BitProcess, "Native x64 Windows collector required");
        var started = DateTimeOffset.UtcNow; var watch = Stopwatch.StartNew(); var budget = new Budget(() => watch.Elapsed, cancellationToken);
        budget.Check(); Need(expectedPid.HasValue == expectedStart.HasValue, "PID/start pair required");
        // Exact expected epoch is checked before ANY copy-path I/O or native open.
        var bytes = IdentityBeforeCopy(() => Pin(expectedPid, expectedStart, () => Discover(expectedPid, expectedStart)), i => ReadCopy(copyPath, i.ImagePath, budget), out var identity);
        var image = PeInspector.Analyze(bytes, Path.GetFileName(copyPath));
        Need(image.Sha256.Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase), "PE inspector hash mismatch");
        StructureInfo structure = Known300Adapter.Unknown(structures ? "Not captured" : "Not requested");
        RegionInfo[] regions = Array.Empty<RegionInfo>(); CodeCheck[] code = Array.Empty<CodeCheck>(); bool bound = false; var status = "Blocked";
        const string scope = "Pinned process epoch and PEB main image; complete PE headers (ImageBase-only normalization) and full bounded raw .rsrc matched before/after. Main-image region metadata and finite known-build function hashes only. Not a whole-runtime hash, atomic capture, or gameplay inventory.";
        try
        {
            budget.Check(); void PinNow() { budget.Check(); Need(Pin(identity.Pid, identity.Start, () => Discover(identity.Pid, identity.Start)) == identity, "Process/module epoch changed"); budget.Check(); }
            PinNow(); using var handle = OpenProcess(0x410, false, identity.Pid); Need(!handle.IsInvalid, "Read/query process handle denied");
            var session = new NativeSession(handle, identity, budget, cancellationToken);
            using var pe = new PEReader(new MemoryStream(bytes, writable: false)); var h = pe.PEHeaders.PEHeader ?? throw new InvalidDataException("PE header missing");
            Need(pe.PEHeaders.CoffHeader.Machine == Machine.Amd64 && h.Magic == PEMagic.PE32Plus && h.SizeOfImage == identity.Size, "Native x64 image size mismatch");
            Need(h.SizeOfHeaders > 0 && h.SizeOfHeaders <= 1024 * 1024 && h.SizeOfHeaders <= bytes.Length && h.SizeOfHeaders <= h.SizeOfImage, "Complete header bounds");
            int optional = checked(BitConverter.ToInt32(bytes, 60) + 24);
            Need(optional >= 24 && (long)optional + 32 <= h.SizeOfHeaders && (long)optional + pe.PEHeaders.CoffHeader.SizeOfOptionalHeader + 40L * pe.PEHeaders.CoffHeader.NumberOfSections <= h.SizeOfHeaders, "Complete PE header extent required");
            var header = bytes.AsSpan(0, h.SizeOfHeaders).ToArray(); BitConverter.GetBytes(identity.Module).CopyTo(header, optional + 24);
            var resources = pe.PEHeaders.SectionHeaders.Where(s => s.Name == ".rsrc").ToArray(); Need(resources.Length == 1, "Exactly one bounded resource section required"); var rs = resources[0];
            Need(rs.SizeOfRawData > 0 && rs.SizeOfRawData <= 2 * 1024 * 1024 && rs.VirtualAddress >= h.SizeOfHeaders && (long)rs.VirtualAddress + rs.SizeOfRawData <= h.SizeOfImage && rs.PointerToRawData >= 0 && (long)rs.PointerToRawData + rs.SizeOfRawData <= bytes.Length, "Full raw resource bounds");
            var resource = bytes.AsSpan(rs.PointerToRawData, rs.SizeOfRawData).ToArray(); ulong? peb = null; byte[]? pebFields = null;
            void Bind()
            {
                PinNow(); var p = session.Peb(); Need(peb is null || peb == p, "PEB changed"); peb = p;
                var fields = session.Read(checked(p + 0x10), 8); Need(BitConverter.ToUInt64(fields) == identity.Module, "PEB main base mismatch");
                Need(pebFields is null || pebFields.AsSpan().SequenceEqual(fields), "PEB main fields changed"); pebFields = fields;
                Need(header.AsSpan().SequenceEqual(session.Read(identity.Module, header.Length)), "Complete runtime headers differ");
                Need(resource.AsSpan().SequenceEqual(session.Read(checked(identity.Module + (uint)rs.VirtualAddress), resource.Length)), "Full runtime resources differ"); PinNow();
            }
            Bind(); regions = EnumerateRegions(identity.Module, identity.Size, session.Query, budget.Check);
            code = CheckCode(image, identity.Module, session.Read);
            Stopwatch? structureClock = structures ? Stopwatch.StartNew() : null;
            if (structures) structure = Known300Adapter.Capture(image, identity.Module, identity.Pid, identity.Start, session.Read, budget.Check, PinNow, cancellationToken, setReadCheck: c => session.AdditionalCheck = c);
            Bind(); budget.Check();
            if (structureClock is not null && structureClock.Elapsed >= TimeSpan.FromSeconds(3)) structure = Known300Adapter.Unknown("Structure expired during closing binding", structureClock.Elapsed.TotalMilliseconds); bound = true; status = "Complete";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or OverflowException or OperationCanceledException)
        {
            // Nothing gathered under an incomplete/expired binding is exported as valid.
            regions = Array.Empty<RegionInfo>(); code = Array.Empty<CodeCheck>(); structure = Known300Adapter.Unknown(ex is OperationCanceledException ? "Cancelled" : "Capture binding/budget unavailable");
            status = ex is OperationCanceledException ? "Cancelled" : "Blocked: " + SafeReason(ex);
        }
        return new Snapshot(1, "0.1.0", Guid.NewGuid().ToString("N"), "live", label, started, DateTimeOffset.UtcNow, image,
            new LiveInfo(identity.Pid, identity.Start, identity.Size, bound, bound, scope, regions, code, structure, budget.Bytes, budget.Queries, budget.Milliseconds, status));
    }
    // Avoid native exception messages containing absolute pointers or paths.
    private static string SafeReason(Exception ex) => ex is InvalidDataException or UnreadableException ? ex.Message : ex.GetType().Name;
    private sealed class NativeSession
    {
        private readonly SafeProcessHandle handle; private readonly Identity identity; private readonly Budget budget; private readonly CancellationToken token;
        internal NativeSession(SafeProcessHandle h, Identity i, Budget b, CancellationToken t) { handle = h; identity = i; budget = b; token = t; }
        internal Action? AdditionalCheck;
        internal MemoryRegion? Query(ulong address)
        {
            AdditionalCheck?.Invoke(); budget.Query(); var got = VirtualQueryEx(handle, (nint)address, out var m, (nuint)Marshal.SizeOf<Mbi>()); budget.Check(); AdditionalCheck?.Invoke();
            return got == (nuint)Marshal.SizeOf<Mbi>() ? new(m.Base, m.Size, m.State, m.Protect, m.Type) : null;
        }
        internal byte[] Read(ulong address, int count) => ReadExact(address, count, Query, (a, n) =>
        { AdditionalCheck?.Invoke(); var data = new byte[n]; Need(ReadProcessMemory(handle, (nint)a, data, n, out var got) && got == n, "Failed or partial native read"); AdditionalCheck?.Invoke(); return data; }, budget, token);
        internal ulong Peb()
        {
            var pbi = new byte[48]; budget.Query(); budget.Charge(48);
            Need(NtQueryInformationProcess(handle, 0, pbi, 48, out var got) >= 0 && got == 48 && BitConverter.ToUInt64(pbi, 32) == (ulong)identity.Pid, "PBI identity unavailable"); budget.Check();
            var wow = new byte[8]; budget.Query(); budget.Charge(8);
            Need(NtQueryInformationProcess(handle, 26, wow, 8, out got) >= 0 && got == 8 && BitConverter.ToUInt64(wow) == 0, "Native x64 PEB required"); budget.Check();
            var p = BitConverter.ToUInt64(pbi, 8); Range(p, 0x18); return p;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Mbi { public ulong Base, Allocation; public uint AllocationProtect; public ushort Partition; public ulong Size; public uint State, Protect, Type; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool ReadProcessMemory(SafeProcessHandle h, nint a, byte[] b, int n, out nint got);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern nuint VirtualQueryEx(SafeProcessHandle h, nint a, out Mbi m, nuint n);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle h, int kind, byte[] b, uint n, out uint got);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle h, out FileInfoNative info);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder path, int size, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern uint GetDriveType(string root);
}
