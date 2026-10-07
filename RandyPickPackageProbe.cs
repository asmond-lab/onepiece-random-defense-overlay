using System.Reflection;
using System.Resources;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>
/// Opt-in package inspection, not a normal application launch. No runtime objects are created.
/// The negative JSON flags describe this code path, not an OS sandbox or security attestation.
/// Output is the sole write: an explicitly supplied, new, local JSON file. No directories are made.
/// </summary>
internal static class RandyPickPackageProbe
{
    internal const string Flag = "--verify-package";
    internal const int MaximumOutputPathLength = 4096;
    internal const int Success = 0;
    internal const int InvalidArguments = 2;
    internal const int MetadataFailure = 3;
    internal const int OutputFailure = 4;

    // Handled result keeps dispatch testable without constructing Application or Execution.
    // A recognizable probe token anywhere owns the invocation, even when malformed.
    internal static bool TryHandleStartup(string[] args, out int exitCode) =>
        TryHandleStartup(args, ReadPackagedMetadata, CreateNewOutput, out exitCode);

    internal static bool TryHandleStartup(string[] args, Func<Metadata> readMetadata,
        Func<string, Stream> createNewOutput, out int exitCode)
    {
        exitCode = Success;
        if (!args.Any(IsProbeToken)) return false;
        exitCode = InvalidArguments;
        if (!TryParseOutputPath(args, out var outputPath)) return true;

        byte[] json;
        try
        {
            var metadata = readMetadata();
            if (metadata.AssemblyName != "OrandOverlay" ||
                string.IsNullOrWhiteSpace(metadata.Version) ||
                string.IsNullOrWhiteSpace(metadata.AssetName) || !metadata.LogoResourcesVerified)
            {
                exitCode = MetadataFailure;
                return true;
            }
            var supported = UpdateChannelVersion.TryParse(metadata.Version, out var identity);
            var report = new
            {
                metadata.Version,
                metadata.AssetName,
                metadata.AssemblyName,
                SelectedChannel = supported ? identity.Channel : null,
                ManifestUrl = supported ? identity.ManifestUrl : null,
                SupportsAutomaticUpdates = supported,
                metadata.CanSelfInstall,
                metadata.LogoResourcesVerified,
                RuntimeStarted = false,
                NoNetwork = true,
                NoGameReads = true,
                NoUserSettingsOpened = true
            };
            json = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            exitCode = MetadataFailure;
            return true;
        }

        try
        {
            // FileMode.CreateNew is atomic against an existing destination. Never delete on failure:
            // an existing file belongs to the caller, and a partial newly-created report is not success.
            using var output = createNewOutput(outputPath);
            output.Write(json, 0, json.Length);
            output.Flush();
        }
        catch
        {
            exitCode = OutputFailure;
            return true;
        }
        exitCode = Success;
        return true;
    }

    private static bool IsProbeToken(string? arg) => arg is not null &&
        (arg.Equals(Flag, StringComparison.OrdinalIgnoreCase) ||
         arg.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase));

    internal static bool TryParseOutputPath(string[] args, out string outputPath)
    {
        outputPath = "";
        if (args.Length != 2 || args[0] != Flag) return false;
        var candidate = args[1];
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaximumOutputPathLength) return false;
        try
        {
            // Drive-qualified local paths only: reject UNC/device paths, drive-relative paths,
            // alternate data streams, ambiguous trailing characters and reserved device names.
            if (candidate.Length < 4 || !char.IsAsciiLetter(candidate[0]) || candidate[1] != ':' ||
                candidate[2] is not ('\\' or '/') || !Path.IsPathFullyQualified(candidate) ||
                candidate[3..].Contains(':') || candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return false;
            var segments = candidate[3..].Split(new[] { '\\', '/' });
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                    segment.EndsWith('.') || segment.EndsWith(' ') ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
                var stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                     stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                     (stem[3] is >= '0' and <= '9' or '¹' or '²' or '³'))) return false;
            }
            if (!Path.GetExtension(candidate).Equals(".json", StringComparison.OrdinalIgnoreCase)) return false;
            outputPath = Path.GetFullPath(candidate);
            return outputPath.Length <= MaximumOutputPathLength;
        }
        catch { return false; }
    }

    internal static Stream CreateNewOutput(string outputPath)
    {
        // Refuse mapped network drives and reparse-point parents instead of following them.
        // These are only metadata checks along the caller's output path, not settings reads.
        var drive = new DriveInfo(Path.GetPathRoot(outputPath)!);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new IOException("Package probe output must be on a local drive.");
        var parents = new Stack<string>();
        for (var parent = Path.GetDirectoryName(outputPath); parent is not null;
             parent = Path.GetDirectoryName(parent)) parents.Push(parent);
        // Root first: do not inspect descendants through an unchecked intermediate link.
        foreach (var parent in parents)
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Package probe output cannot traverse a reparse point.");
        }
        return new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    private static Metadata ReadPackagedMetadata()
    {
        var assembly = typeof(RandyPickPackageProbe).Assembly;
        var processPath = Environment.ProcessPath ?? throw new InvalidDataException("Missing process path.");
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? throw new InvalidDataException("Missing package version.");
        // Mirrors UpdateService.CanSelfInstall using only adjacent packaged-assembly existence.
        // Do not instantiate UpdateService or read its trust, logs, transport or user paths.
        var canSelfInstall = !File.Exists(Path.Combine(Path.GetDirectoryName(processPath) ?? "", "OrandOverlay.dll"));
        using var stream = assembly.GetManifestResourceStream("OrandOverlay.g.resources")
            ?? throw new InvalidDataException("Missing packaged WPF resources.");
        using var reader = new ResourceReader(stream);
        var found = new HashSet<string>(StringComparer.Ordinal);
        var entries = reader.GetEnumerator();
        while (entries.MoveNext())
        {
            // Read keys only. Never deserialize BAML or instantiate resource objects/windows.
            if (entries.Key is string key) found.Add(key);
        }
        var logos = found.Contains("assets/randypick-logo-64.png") &&
                    found.Contains("assets/randypick-logo-256.png");
        return new(version, Path.GetFileName(processPath), assembly.GetName().Name ?? "", canSelfInstall, logos);
    }

    internal sealed record Metadata(string Version, string AssetName, string AssemblyName,
        bool CanSelfInstall, bool LogoResourcesVerified);
}
