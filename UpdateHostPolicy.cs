using System.Reflection;

namespace OrandOverlay;

/// <summary>Pure capability policy: a loaded updater DLL never grants permission to replace its host.</summary>
internal static class UpdateHostPolicy
{
    internal static bool CanSelfInstall(Assembly? entryAssembly, Assembly applicationAssembly,
        string? processPath, bool processFileExists, bool adjacentApplicationDllExists)
    {
        if (!ReferenceEquals(entryAssembly, applicationAssembly) || !processFileExists || adjacentApplicationDllExists ||
            string.IsNullOrWhiteSpace(processPath)) return false;
        try
        {
            return processPath.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(processPath) &&
                string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
    }
}
