namespace OrandOverlay;

/// <summary>Public apphost branding is independent of assembly and resource identity.</summary>
internal static class UpdateAssetPolicy
{
    internal const string CurrentName = "RandyPick.exe";
    internal const string LegacyName = "OrandOverlay.exe";
    internal static bool IsAllowedApplicationName(string? name) => name is CurrentName or LegacyName;

    internal static bool TryGetApplicationName(string version, string assetPath, out string name)
    {
        name = assetPath[(assetPath.LastIndexOf('/') + 1)..];
        // Exact signed path equality rejects casing aliases, escaping, queries and traversal.
        return IsAllowedApplicationName(name) && assetPath == $"/downloads/{version}/{name}";
    }
}
