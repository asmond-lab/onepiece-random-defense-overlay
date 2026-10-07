using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PlannerEvidenceCapture;

internal readonly record struct CaptureSourceInput(string Path, byte[] Content);

internal static class CaptureSourceFingerprint
{
    private static readonly System.Reflection.AssemblyMetadataAttribute[] Inputs =
        typeof(CaptureSourceFingerprint).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .Where(item => item.Key.StartsWith("CaptureSource:", StringComparison.Ordinal)).ToArray();

    internal static IReadOnlyList<string> CanonicalPaths { get; } =
        Inputs.Select(item => item.Key["CaptureSource:".Length..]).ToArray();

    internal static string FromCanonicalFiles() => FromBundle(AppContext.BaseDirectory);

    internal static string FromBundle(string bundleRoot)
    {
        ValidateSyntax(bundleRoot);
        var pins = new List<SafeFileHandle>();
        try
        {
            var root = Path.Combine(bundleRoot, "FingerprintInputs");
            PinChain(root, pins);
            ValidatePackage(root, pins);
            return Compute(Inputs.Select(item =>
            {
                var path = item.Key["CaptureSource:".Length..];
                var fullPath = Path.Combine(root, path + ".input");
                ValidateSyntax(fullPath);
                // ValidatePackage retains no-write/no-delete handles for every input.
                var content = File.ReadAllBytes(fullPath);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(content)), item.Value, StringComparison.Ordinal))
                    throw new InvalidDataException("Capture fingerprint input differs from its build binding: " + path);
                return new CaptureSourceInput(path, content);
            }));
        }
        finally { for (var i = pins.Count - 1; i >= 0; i--) pins[i].Dispose(); }
    }

    private static void PinChain(string directory, List<SafeFileHandle> pins)
    {
        var chain = new Stack<string>();
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
            chain.Push(current);
        while (chain.TryPop(out var current)) pins.Add(OpenPlain(current, true));
    }

    private static void ValidatePackage(string root, List<SafeFileHandle> pins)
    {
        var expectedFiles = CanonicalPaths.Select(path => path + ".input").ToHashSet(StringComparer.Ordinal);
        var expectedDirectories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in expectedFiles)
            for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                expectedDirectories.Add(parent.Replace('\\', '/'));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Visit(root);
        if (!seen.SetEquals(expectedFiles)) throw new IOException("Capture fingerprint package is incomplete.");

        void Visit(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ValidateSyntax(entry);
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                var isDirectory = File.GetAttributes(entry).HasFlag(FileAttributes.Directory);
                pins.Add(OpenPlain(entry, isDirectory));
                if (isDirectory)
                {
                    if (!expectedDirectories.Contains(relative)) throw new InvalidDataException("Unexpected fingerprint directory.");
                    Visit(entry);
                }
                else if (!expectedFiles.Contains(relative) || !seen.Add(relative))
                    throw new InvalidDataException("Unexpected fingerprint file.");
            }
        }
    }

    private static SafeFileHandle OpenPlain(string path, bool directory)
    {
        var handle = CreateFileW(path, 0x80000000, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("Cannot open capture fingerprint input.", new Win32Exception(error));
        }
        try
        {
            if (!GetFileInformationByHandleEx(handle, 9, out var info, 8))
                throw new IOException("Cannot inspect capture fingerprint input.", new Win32Exception(Marshal.GetLastWin32Error()));
            var attributes = (FileAttributes)info.Attributes;
            if ((attributes & FileAttributes.ReparsePoint) != 0 || attributes.HasFlag(FileAttributes.Directory) != directory)
                throw new IOException("Capture fingerprint inputs must be plain files and directories.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTagInfo information, uint size);

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

    internal static string Compute(IEnumerable<CaptureSourceInput> inputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var input in inputs.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            Append(hash, Encoding.UTF8.GetBytes(input.Path.Replace('\\', '/')));
            Append(hash, input.Content);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, byte[] value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}
