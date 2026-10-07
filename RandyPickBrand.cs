namespace OrandOverlay;

/// <summary>Shared presentation labels derived from the application's numeric version.</summary>
public static class RandyPickBrand
{
    public static string BetaLabel => VersionLabel(UpdateService.CurrentVersion);
    public static string ProductLabel => $"랜디픽 {BetaLabel}";

    public static string VersionLabel(Version version) =>
        $"BETA {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    public static string UpdateLabel(string? identity) =>
        UpdateChannelVersion.TryParse(identity, out var version) ? VersionLabel(version.Core) : "새 버전";
}
