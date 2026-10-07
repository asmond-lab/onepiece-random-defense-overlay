using System.Reflection;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class LiveValidationProfileTests
{
    [Fact]
    public void IsolatedValidationProfileOverridesBundledReferenceWithoutGrantingProductionAccess()
    {
        var program = typeof(StandaloneScalarRunner).Assembly.GetType("Program", throwOnError: true)!;
        var json = (string)program.GetField("ProfileJson", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var directory = Path.Combine(Path.GetTempPath(), "randypick-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "memory-profiles.json");
            File.WriteAllText(path, json);
            var snapshot = new MemoryProfileRepository(path).GetProfiles();
            Assert.Null(snapshot.Error);
            var profile = Assert.Single(snapshot.Profiles, p => p.FileVersion == Warcraft300Diagnostic.Version);
            Assert.Equal("explicit-live-validation-only", profile.ProfileId);
            Assert.Empty(MemoryProfileValidator.Validate(profile));
            Assert.True(Warcraft300Diagnostic.SessionAllows(profile, true));
            Assert.False(Warcraft300Diagnostic.SessionAllows(profile, false));
            Assert.False(MemoryProfileValidator.CanActivate(profile, out _));
            Assert.False(profile.Verified);
            var bundled = Assert.Single(new MemoryProfileRepository().GetProfiles().Profiles,
                p => p.FileVersion == Warcraft300Diagnostic.Version);
            Assert.False(bundled.Enabled);
            Assert.False(bundled.Verified);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
