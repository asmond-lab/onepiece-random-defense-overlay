using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WarcraftProbe;

internal static class ProbeFiles
{
    internal static string LocalPath(string supplied, bool absoluteRequired = true)
    {
        if (string.IsNullOrWhiteSpace(supplied) || supplied.Length > 30000 || (absoluteRequired && !Path.IsPathFullyQualified(supplied))) throw new ArgumentException("Absolute local path required");
        if (supplied.StartsWith("\\\\", StringComparison.Ordinal) || supplied.Contains('/') || supplied.Any(char.IsControl)) throw new ArgumentException("Local DOS path required");
        var full = Path.GetFullPath(supplied);
        if (full.Length < 3 || !char.IsAsciiLetter(full[0]) || full[1] != ':' || full[2] != (char)92 || full.AsSpan(2).Contains(':')) throw new ArgumentException("Local drive path required");
        foreach (var part in supplied.Split((char)92))
        {
            if (part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.')) throw new ArgumentException("Ambiguous path segment");
            if (part.Equals("MemoryDiagnostics", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Excluded path");
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')) throw new ArgumentException("Device name");
        }
        return full;
    }
    internal static void FixedDrive(string full)
    {
        if (GetDriveType(Path.GetPathRoot(full)!) != 3) throw new UnauthorizedAccessException("Fixed local drive required");
    }
    internal static void NoReparseAncestors(string full, bool includeFile)
    {
        FileSystemInfo? item = includeFile ? new FileInfo(full) : new DirectoryInfo(full);
        for (; item is not null; item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent)
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Reparse path");
        }
    }
    internal static byte[] ReadLocalFile(string path, long maximum)
    {
        var full = LocalPath(path); FixedDrive(full); NoReparseAncestors(full, true);
        using var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var info) || info.Links != 1 || (info.Attributes & 0x450) != 0) throw new UnauthorizedAccessException("Regular single-link file required");
        var final = new System.Text.StringBuilder(32768);
        var count = GetFinalPathNameByHandle(file.SafeFileHandle, final, final.Capacity, 0);
        var value = final.ToString();
        if (count == 0 || count >= final.Capacity || !value.StartsWith(new string((char)92, 2) + "?" + (char)92) || !string.Equals(LocalPath(value[4..]), full, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("File path changed");
        if (file.Length < 1 || file.Length > maximum || file.Length > int.MaxValue) throw new InvalidDataException("File size cap");
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new InvalidDataException("File changed"); return bytes;
    }
    internal static void SaveNew(string requested, string jsonName, byte[] json, byte[] report)
    {
        if (jsonName is not ("snapshot.json" or "comparison.json") || json.Length > 16 * 1024 * 1024 || report.Length > 16 * 1024 * 1024) throw new ArgumentException("Output contract");
        var target = LocalPath(requested, false); FixedDrive(target);
        if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Output exists");
        var parent = Path.GetDirectoryName(target) ?? throw new ArgumentException("Output parent");
        NoReparseAncestors(parent, false); Directory.CreateDirectory(parent); NoReparseAncestors(parent, false);
        var temporary = Path.Combine(parent, ".warcraftprobe-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(temporary) || File.Exists(temporary)) throw new IOException("Temporary collision");
        Directory.CreateDirectory(temporary);
        var owned = new List<string>();
        try
        {
            void Write(string name, byte[] bytes)
            {
                var p = Path.Combine(temporary, name); using var s = new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                owned.Add(p); s.Write(bytes); s.Flush(true);
            }
            Write(jsonName, json); Write("report.md", report);
            var zipPath = Path.Combine(temporary, "report.zip");
            using (var zipFile = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned.Add(zipPath); using var archive = new ZipArchive(zipFile, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var pair in new[] { (jsonName, json), ("report.md", report) })
                { var entry = archive.CreateEntry(pair.Item1, CompressionLevel.Optimal); using var stream = entry.Open(); stream.Write(pair.Item2); }
            }
            NoReparseAncestors(parent, false); Directory.Move(temporary, target);
        }
        catch
        {
            foreach (var p in owned) { try { File.Delete(p); } catch { } }
            try { Directory.Delete(temporary, false); } catch { }
            throw;
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written; public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetFileInformationByHandle(SafeFileHandle h, out FileInfoNative info);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern uint GetFinalPathNameByHandle(SafeFileHandle h, System.Text.StringBuilder path, int size, uint flags);
}
