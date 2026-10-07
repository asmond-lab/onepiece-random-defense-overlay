using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ShippedReferenceProfileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualShippedProfilesRouteExactModernBuildWithMissingOrLegacyOnlyCache(bool legacyCache)
    {
        var root = Path.Combine(Path.GetTempPath(), "orand-shipped-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "Data", "memory-profiles.json");
            var user = Path.Combine(root, "memory-profiles.json");
            var bundledBefore = File.ReadAllText(bundled);
            using var document = JsonDocument.Parse(bundledBefore);
            var legacyJson = document.RootElement.EnumerateArray().Single(p =>
                p.GetProperty("fileVersion").GetString() == "2.0.4.23745").GetRawText();
            if (legacyCache) File.WriteAllText(user, "[" + legacyJson + "]");
            var userBefore = legacyCache ? File.ReadAllText(user) : null;

            var loaded = new MemoryProfileRepository(user, bundled).GetProfiles();

            Assert.Null(loaded.Error);
            var modern = Assert.Single(loaded.Profiles, p => p.FileVersion == Warcraft300Diagnostic.Version);
            Assert.True(modern.KnownExperimental);
            Assert.False(modern.Enabled);
            Assert.False(modern.Verified);
            Assert.Empty(MemoryProfileValidator.Validate(modern));
            Assert.Equal(Warcraft300Diagnostic.Hash, modern.ExecutableSha256);
            Assert.True(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.320",
                DiagnosticInventoryObservation.PinnedDatasetFingerprint, modern,
                Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash));
            Assert.False(MapDatasetRuntimePolicy.AllowsReferenceObservation("2.314",
                DiagnosticInventoryObservation.PinnedDatasetFingerprint, modern,
                Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash));
            Assert.False(Warcraft300Diagnostic.SessionAllows(modern, false));
            Assert.False(Warcraft300Diagnostic.SessionAllows(modern, true));
            Assert.False(MemoryProfileValidator.CanActivate(modern, out _));
            var legacy = Assert.Single(loaded.Profiles, p => p.FileVersion == "2.0.4.23745");
            Assert.True(legacy.Enabled);
            Assert.True(legacy.Verified);
            Assert.Equal(5, legacy.ProfileRevision);
            Assert.True(MemoryProfileValidator.CanActivate(legacy, out _));
            Assert.Equal(bundledBefore, File.ReadAllText(bundled));
            if (legacyCache) Assert.Equal(userBefore, File.ReadAllText(user));
            else Assert.False(File.Exists(user));
        }
        finally { Directory.Delete(root, true); }
    }
}
