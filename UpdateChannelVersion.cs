using System.Text.RegularExpressions;

namespace OrandOverlay;

/// <summary>Local identity selects policy. Remote manifests never select a channel.</summary>
internal sealed record UpdateChannelVersion(string Text, string Channel, Version Core, string[] Prerelease)
{
    internal static bool TryParse(string? text, out UpdateChannelVersion value)
    {
        value = null!;
        if (text is not { Length: > 0 and <= 64 }) return false;
        var marker = text.IndexOf("-test.", StringComparison.Ordinal);
        var coreText = marker < 0 ? text : text[..marker];
        if (!SignedUpdateManifest.TryVersion(coreText, out var core)) return false;
        var segments = marker < 0 ? Array.Empty<string>() : text[(marker + 6)..].Split('.');
        foreach (var segment in segments)
        {
            if (!Regex.IsMatch(segment, @"\A[A-Za-z0-9]+\z", RegexOptions.CultureInvariant)) return false;
            if (IsNumeric(segment) && segment.Length > 1 && segment[0] == '0') return false;
        }
        value = new(text, marker < 0 ? "stable" : "test", core, segments);
        return true;
    }

    private static bool IsNumeric(string text) => text.All(c => c is >= '0' and <= '9');
    internal int CompareTo(UpdateChannelVersion other)
    {
        var core = Core.CompareTo(other.Core);
        if (core != 0) return core;
        if (Prerelease.Length == 0 || other.Prerelease.Length == 0)
            return Prerelease.Length == other.Prerelease.Length ? 0 : Prerelease.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(Prerelease.Length, other.Prerelease.Length); i++)
        {
            var a = Prerelease[i]; var b = other.Prerelease[i];
            var an = IsNumeric(a); var bn = IsNumeric(b);
            var comparison = an && bn ? (a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b))
                : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a, b);
            if (comparison != 0) return comparison;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }
    internal bool IsUpgradeFrom(UpdateChannelVersion current) => Channel == current.Channel && CompareTo(current) > 0;
    internal string ManifestUrl => Channel == "test" ? SignedUpdateManifest.TestApplicationManifestUrl : SignedUpdateManifest.ApplicationManifestUrl;
}
