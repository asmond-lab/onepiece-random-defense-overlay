using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

const string expected = "55D0FFB9921433F45A9244CB946BDD27DCD2552A3550D30C4617C2EACCB94E97";
var output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Source artifact already exists.");
Native.SFileSetLocale(1042);
if (!Native.SFileOpenArchive(Path.GetFullPath(args[0]), 0, 0x100, out var archive))
    throw new Win32Exception(Marshal.GetLastWin32Error());
byte[] bytes;
try
{
    if (!Native.SFileOpenFileEx(archive, "war3map.j", 0, out var file))
        throw new Win32Exception(Marshal.GetLastWin32Error());
    try
    {
        uint high = 0;
        var size = Native.SFileGetFileSize(file, ref high);
        if (high != 0 || size is 0 or > 32 * 1024 * 1024)
            throw new InvalidDataException("Unexpected script size.");
        bytes = new byte[size];
        if (!Native.SFileReadFile(file, bytes, size, out var read, IntPtr.Zero) || read != size)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    finally { Native.SFileCloseFile(file); }
}
finally { Native.SFileCloseArchive(archive); }
var hash = Convert.ToHexString(SHA256.HashData(bytes));
if (hash != expected) throw new InvalidDataException($"Script pin mismatch: {hash}");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
    target.Write(bytes);
Console.WriteLine($"EXTRACTED bytes={bytes.Length} sha256={hash}");

internal static class Native
{
    private const string Library = "StormLib.dll";
    [DllImport(Library)]
    internal static extern uint SFileSetLocale(uint locale);
    [DllImport(Library, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileOpenArchive(string name, uint priority, uint flags, out IntPtr archive);
    [DllImport(Library, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileOpenFileEx(IntPtr archive, string name, uint scope, out IntPtr file);
    [DllImport(Library, SetLastError = true)]
    internal static extern uint SFileGetFileSize(IntPtr file, ref uint high);
    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileReadFile(IntPtr file, byte[] buffer, uint count, out uint read, IntPtr overlapped);
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileCloseFile(IntPtr file);
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool SFileCloseArchive(IntPtr archive);
}
