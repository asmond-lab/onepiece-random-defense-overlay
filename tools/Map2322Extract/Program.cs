using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Usage: Map2322Extract <archive.w3x> <new-output-directory>");
var archivePath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (Directory.Exists(outputPath)) throw new IOException("Output directory already exists.");
var archiveHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivePath))).ToLowerInvariant();
Directory.CreateDirectory(outputPath);
Native.SFileSetLocale(1042);
if (!Native.SFileOpenArchive(archivePath, 0, 0x100, out var archive))
    throw new Win32Exception(Marshal.GetLastWin32Error());
var members = new List<object>();
try
{
    foreach (var name in new[] { "war3map.j", "war3map.w3u", "war3map.w3a", "war3map.w3t", "war3map.w3q", "war3map.wts", "war3map.w3i", "war3mapMisc.txt" })
    {
        if (!Native.SFileOpenFileEx(archive, name, 0, out var file))
            throw new IOException($"Archive member missing: {name}", new Win32Exception(Marshal.GetLastWin32Error()));
        try
        {
            uint high = 0;
            var size = Native.SFileGetFileSize(file, ref high);
            if (high != 0 || size is 0 or > 64 * 1024 * 1024)
                throw new InvalidDataException($"Unexpected member size for {name}: {high}:{size}");
            var bytes = new byte[size];
            if (!Native.SFileReadFile(file, bytes, size, out var read, IntPtr.Zero) || read != size)
                throw new IOException($"Cannot read {name}", new Win32Exception(Marshal.GetLastWin32Error()));
            var destination = Path.Combine(outputPath, name);
            using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) target.Write(bytes);
            members.Add(new { name, bytes = size, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        finally { Native.SFileCloseFile(file); }
    }
}
finally { Native.SFileCloseArchive(archive); }
var manifest = new { archive = Path.GetFileName(archivePath), archiveBytes = new FileInfo(archivePath).Length, archiveSha256 = archiveHash, members };
using (var report = new FileStream(Path.Combine(outputPath, "extraction-manifest.json"), FileMode.CreateNew, FileAccess.Write))
    JsonSerializer.Serialize(report, manifest, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(JsonSerializer.Serialize(manifest));

internal static class Native
{
    private const string Library = "StormLib.dll";
    [DllImport(Library)] internal static extern uint SFileSetLocale(uint locale);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SFileOpenArchive(string name, uint priority, uint flags, out IntPtr archive);
    [DllImport(Library, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SFileOpenFileEx(IntPtr archive, string name, uint scope, out IntPtr file);
    [DllImport(Library, SetLastError = true)] internal static extern uint SFileGetFileSize(IntPtr file, ref uint high);
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SFileReadFile(IntPtr file, byte[] buffer, uint count, out uint read, IntPtr overlapped);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SFileCloseFile(IntPtr file);
    [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SFileCloseArchive(IntPtr archive);
}
