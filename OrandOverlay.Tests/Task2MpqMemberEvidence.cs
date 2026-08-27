using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OrandOverlay.Tests;

public sealed record MpqMemberEvidence(string Name, long Length, string Sha256);

public static class Task2MpqMemberEvidence
{
    private const string StormLib =
        @"C:\Program Files (x86)\Warcraft III\_retail_\x86_64\JassHelper\sfmpq.dll";

    public static IReadOnlyList<MpqMemberEvidence> Read(
        string archivePath, IReadOnlyList<string> names)
    {
        if (!File.Exists(archivePath) || names.Count == 0 ||
            names.Any(string.IsNullOrWhiteSpace) ||
            names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new InvalidDataException("invalid MPQ evidence request");
        try { return ReadNative(archivePath, names); }
        catch (BadImageFormatException) { return ReadHelper(archivePath, names); }
    }

    private static IReadOnlyList<MpqMemberEvidence> ReadNative(
        string archivePath, IReadOnlyList<string> names)
    {
        if (!Native.SFileOpenArchive(archivePath, 0, 0, out var archive))
            throw new InvalidDataException("MPQ archive could not be opened");
        try
        {
            return names.Select(name => ReadMember(archive, name)).ToArray();
        }
        finally { Native.SFileCloseArchive(archive); }
    }

    private static MpqMemberEvidence ReadMember(IntPtr archive, string name)
    {
        if (!Native.SFileOpenFileEx(archive, name, 0, out var file))
            throw new InvalidDataException($"MPQ member missing: {name}");
        try
        {
            var high = 0U;
            var low = Native.SFileGetFileSize(file, ref high);
            var size = ((ulong)high << 32) | low;
            if (size > 512 * 1024 * 1024)
                throw new InvalidDataException($"MPQ member too large: {name}");
            var bytes = new byte[(int)size];
            if (!Native.SFileReadFile(file, bytes, (uint)bytes.Length, out var read,
                    IntPtr.Zero) ||
                read != bytes.Length)
                throw new InvalidDataException($"MPQ member read failed: {name}");
            return new(name, bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        finally { Native.SFileCloseFile(file); }
    }

    private static class Native
    {
        [DllImport(StormLib, CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern bool SFileOpenArchive(string fileName, uint priority,
            uint flags, out IntPtr archive);
        [DllImport(StormLib, CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern bool SFileOpenFileEx(IntPtr archive, string fileName,
            uint searchScope, out IntPtr file);
        [DllImport(StormLib, SetLastError = true)]
        internal static extern uint SFileGetFileSize(IntPtr file, ref uint highSize);
        [DllImport(StormLib, SetLastError = true)]
        internal static extern bool SFileReadFile(IntPtr file, byte[] buffer,
            uint bytesToRead, out uint bytesRead, IntPtr overlapped);
        [DllImport(StormLib)] internal static extern bool SFileCloseFile(IntPtr file);
        [DllImport(StormLib)] internal static extern bool SFileCloseArchive(IntPtr archive);
    }

    private static IReadOnlyList<MpqMemberEvidence> ReadHelper(
        string archivePath, IReadOnlyList<string> names)
    {
        var relativeProject = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../../work/MpqEvidence"));
        var configuredProject = Environment.GetEnvironmentVariable("ORAND_MPQ_EVIDENCE_DIR");
        var project = new[]
            {
                configuredProject,
                relativeProject,
                @"D:\OrandOverlay\work\MpqEvidence"
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .FirstOrDefault(path => Directory.Exists(path!))
            ?? throw new InvalidDataException("MPQ evidence utility is unavailable");
        var helper = Path.Combine(project, "bin", "Release", "net10.0-windows",
            "win-x86", "MpqEvidence.exe");
        if (!File.Exists(helper))
            throw new InvalidDataException("MPQ evidence utility is not built");
        var start = new ProcessStartInfo(helper,
            $"--hash \"{archivePath}\" {string.Join(" ", names.Select(name => $"\"{name}\""))}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["DOTNET_ROOT_X86"] = @"C:\Program Files (x86)\dotnet";
        start.Environment["DOTNET_ROOT"] = @"C:\Program Files (x86)\dotnet";
        using var process = Process.Start(start)
            ?? throw new InvalidDataException("MPQ hash helper could not start");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidDataException("MPQ hash helper exceeded its bound");
        }
        Task.WaitAll(outputTask, errorTask);
        if (process.ExitCode != 0)
            throw new InvalidDataException(errorTask.Result);
        var members = outputTask.Result
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length == 3)
            .Select(parts => new MpqMemberEvidence(parts[0],
                long.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]))
            .ToArray();
        if (members.Length != names.Count ||
            !members.Select(member => member.Name).SequenceEqual(names) ||
            members.Any(member => member.Length <= 0 || member.Sha256.Length != 64))
            throw new InvalidDataException("MPQ hash helper returned incomplete evidence");
        return members;
    }
}
