using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using OrandOverlay;

namespace PlannerEvidenceCapture;

/// <summary>Lexical fixture input gate; never probes an image supplied by a caller.</summary>
public static class CaptureInputContract
{
    private static readonly Regex UnitKey = new(@"\A(?:[A-Za-z0-9_-]+|rawcode:(?:[A-Za-z0-9]{4}|KB0H_|LUMBER|POINT|RANDOM))\z", RegexOptions.CultureInvariant);

    private static IReadOnlySet<string>? bundledIds;

    public static void ValidateFixtureId(string id, IReadOnlySet<string> allowed)
    {
        ValidateImageInput(id, "");
        ArgumentNullException.ThrowIfNull(allowed);
        if (!allowed.Contains(id)) throw new ArgumentException("Unit ID is not in the trusted bundled fixture allowlist.");
    }

    internal static void InitializeBundledAllowlist()
    {
        // Call only after the bundled JSON image/key preflight, before any WPF.
        var catalog = new DataCatalog();
        catalog.Load();
        var ids = catalog.AllUnits.Select(unit => unit.Id)
            .Concat(catalog.RawcodeCatalog.Keys.Select(code => "rawcode:" + code)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids) ValidateImageInput(id, "");
        bundledIds = ids;
    }

    private static void RequireBundledId(string id) => ValidateFixtureId(id,
        bundledIds ?? throw new InvalidOperationException("Bundled fixture allowlist preflight has not run."));

    public static void ValidateImageInput(string id, string fallback)
    {
        if (string.IsNullOrEmpty(id) || !UnitKey.IsMatch(id)) throw new ArgumentException("Unsafe fixture unit ID.");
        if (fallback != "" && (!Uri.TryCreate(fallback, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.IsFile || uri.IsUnc))
            throw new ArgumentException("Only empty/HTTPS (non-file) bundled fallback metadata is allowed.");
    }

    public static void ValidateBundledInputs(string dataDirectory)
    {
        // Bundle integrity/ownership is a prerequisite, not an arbitrary input directory.
        // Inspect only this explicit bundle, never a path found in its JSON.
        var directory = new DirectoryInfo(dataDirectory);
        for (var current = directory; current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse bundle ancestor.");
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse bundle entry.");
            if (entry is FileInfo file && file.Extension == ".json") ValidateJson(File.ReadAllText(file.FullName));
            if (entry is DirectoryInfo child && child.Name == "images")
                foreach (var image in child.EnumerateFileSystemInfos())
                    if ((image.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                        throw new IOException("Only plain bundled image files are permitted.");
        }
    }

    public static void ValidateJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        Visit(document.RootElement, "");
        static void Visit(JsonElement node, string key)
        {
            if (node.ValueKind == JsonValueKind.Object)
                foreach (var property in node.EnumerateObject()) Visit(property.Value, property.Name);
            else if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) Visit(item, key);
            else if (node.ValueKind == JsonValueKind.String)
            {
                var value = node.GetString()!;
                if (key.Equals("image", StringComparison.OrdinalIgnoreCase)) ValidateImageInput("fixture", value);
                if (key.Equals("rawcode", StringComparison.OrdinalIgnoreCase) || key.Equals("rawcodes", StringComparison.OrdinalIgnoreCase))
                    ValidateImageInput("rawcode:" + value, "");
                if (key.Equals("unitId", StringComparison.OrdinalIgnoreCase) || key.Equals("goalUnitId", StringComparison.OrdinalIgnoreCase))
                    ValidateImageInput(value, "");
                if (key.Equals("id", StringComparison.OrdinalIgnoreCase) &&
                    (value.Contains('/') || value.Contains('\\') || value.Contains("..", StringComparison.Ordinal)))
                    throw new ArgumentException("Unsafe bundled ID.");
            }
        }
    }

    internal static AppSettings ValidateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("UnitId", StringComparison.Ordinal)))
            if (property.GetValue(settings) is string { Length: > 0 } id) RequireBundledId(id);
        return settings;
    }

    internal static RecognitionResult ValidateRecognition(RecognitionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var entry in result.Entries) RequireBundledId(entry.UnitId);
        return result;
    }
}
