using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PlannerEvidenceCapture;

/// <summary>Owns a fresh capture run below an explicitly approved parent.</summary>
public sealed class CaptureOutputScope : IDisposable
{
    private readonly List<SafeFileHandle> pins = [];
    private readonly List<FileStream> writers = [];
    private readonly HashSet<string> ownedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private bool disposed;
    private CaptureOutputScope(string root) => Root = root;
    public string Root { get; }

    public static CaptureOutputScope Create(string approvedParent, string output)
    {
        ValidateSyntax(approvedParent);
        ValidateSyntax(output);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(approvedParent));
        var root = Path.GetFullPath(output);
        if (!string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must be a direct child of the approved parent.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        var scope = new CaptureOutputScope(root);
        try
        {
            scope.PinChain(parent);
            // Win32 creation is exclusive: unlike Directory.CreateDirectory it fails
            // if a file, directory, dangling symlink or junction already occupies the name.
            if (!CreateDirectoryW(root, IntPtr.Zero)) throw NativeError("Cannot create fresh output");
            scope.PinDirectory(root);
            scope.ownedDirectories.Add(root);
            return scope;
        }
        catch { scope.Dispose(); throw; }
    }

    public FileStream CreateNew(string path)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var full = Contained(path);
            EnsureDirectory(Path.GetDirectoryName(full)!);
            var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            writers.Add(stream);
            return stream;
        }
    }

    private string Contained(string path)
    {
        ValidateSyntax(path);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path must be inside the owned run.");
        return full;
    }

    public string EnsureDirectory(string path)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var full = string.Equals(path, Root, StringComparison.OrdinalIgnoreCase) ? Root : Contained(path);
            if (ownedDirectories.Contains(full)) return full;
            EnsureDirectory(Path.GetDirectoryName(full)!);
            if (!CreateDirectoryW(full, IntPtr.Zero)) throw NativeError("Cannot create fresh child directory");
            PinDirectory(full);
            ownedDirectories.Add(full);
            return full;
        }
    }

    private static void ValidateSyntax(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            throw new ArgumentException("Only local drive-absolute paths are allowed.");
        foreach (var part in path[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 "0123456789¹²³".Contains(stem[3])))
                throw new ArgumentException("Reserved device path rejected.");
            if (part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Ambiguous path segment rejected.");
        }
    }

    private void PinChain(string directory)
    {
        var chain = new Stack<string>();
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
            chain.Push(current);
        while (chain.TryPop(out var current)) PinDirectory(current);
    }

    private void PinDirectory(string directory)
    {
        // No FILE_SHARE_WRITE or FILE_SHARE_DELETE: directory replacement and
        // reparse retargeting are blocked until the scope releases every ancestor.
        var handle = CreateFileW(directory, 0x80000000, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw NativeError("Cannot pin directory"); }
        try
        {
            if (!GetFileInformationByHandleEx(handle, 9, out var info, 8)) throw NativeError("Cannot inspect directory handle");
            RequirePlainDirectory((FileAttributes)info.Attributes);
            pins.Add(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    private static void RequirePlainDirectory(FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException("Reparse points (including symlinks and junctions) and non-directories are forbidden.");
    }

    private static IOException NativeError(string message) => new(message, new Win32Exception(Marshal.GetLastWin32Error()));

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTagInfo information, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            try { foreach (var writer in writers) writer.Dispose(); }
            finally
            {
                for (var i = pins.Count - 1; i >= 0; i--) pins[i].Dispose();
                pins.Clear();
            }
        }
    }
}
