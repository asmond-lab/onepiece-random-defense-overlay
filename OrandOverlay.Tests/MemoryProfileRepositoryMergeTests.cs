using Xunit;

namespace OrandOverlay.Tests;

public sealed class MemoryProfileRepositoryMergeTests
{
    [Fact]
    public void BundledNewBuildAndRevisionAreNotMaskedByStaleUserCache()
    {
        using var files = new ProfileFiles();
        files.WriteBundled(
            Profile("3.0.0.24268", 3, "BUNDLED-NEW"),
            Profile("3.0.0.24267", 4, "BUNDLED-REVISION"),
            Profile("3.0.0.24266", 2, "BUNDLED-OLD"));
        files.WriteUser(
            Profile("3.0.0.24267", 2, "USER-STALE"),
            Profile("3.0.0.24266", 3, "USER-NEWER"));
        var userBeforeLoad = File.ReadAllText(files.UserPath);

        var loaded = files.Repository.GetProfiles();

        Assert.Null(loaded.Error);
        Assert.Equal(3, loaded.Profiles.Count);
        Assert.Equal("BUNDLED-NEW", loaded.Profiles.Single(x => x.FileVersion == "3.0.0.24268").ProfileId);
        Assert.Equal("BUNDLED-REVISION", loaded.Profiles.Single(x => x.FileVersion == "3.0.0.24267").ProfileId);
        Assert.Equal("USER-NEWER", loaded.Profiles.Single(x => x.FileVersion == "3.0.0.24266").ProfileId);
        Assert.Equal(userBeforeLoad, File.ReadAllText(files.UserPath));
        Assert.StartsWith("사용자:", loaded.Source);
    }

    [Fact]
    public void MalformedUserCacheFallsBackToBundledAndPreservesTheInvalidFile()
    {
        using var files = new ProfileFiles();
        files.WriteBundled(Profile("3.0.0.24268", 1, "BUNDLED"));
        const string invalid = "{ not json";
        File.WriteAllText(files.UserPath, invalid);

        var loaded = files.Repository.GetProfiles();

        Assert.Null(loaded.Error);
        Assert.Single(loaded.Profiles);
        Assert.Equal("BUNDLED", loaded.Profiles[0].ProfileId);
        Assert.Contains("사용자 캐시 무시", loaded.Source);
        Assert.Equal(invalid, File.ReadAllText(files.UserPath));
    }

    [Fact]
    public void BundledWinsEqualRevisionHashConflictAndDuplicateUserSetFallsBackConservatively()
    {
        using var files = new ProfileFiles();
        files.WriteBundled(Profile("3.0.0.24268", 5, "BUNDLED", 'A'));
        files.WriteUser(Profile("3.0.0.24268", 5, "USER-CONFLICT", 'B'));

        var equalRevision = files.Repository.GetProfiles();

        Assert.Null(equalRevision.Error);
        Assert.Equal("BUNDLED", Assert.Single(equalRevision.Profiles).ProfileId);

        files.WriteUser(Profile("3.0.0.24268", 6, "USER-A"), Profile("3.0.0.24268", 7, "USER-DUPLICATE"));
        var duplicate = files.Repository.GetProfiles();

        Assert.Null(duplicate.Error);
        Assert.Equal("BUNDLED", Assert.Single(duplicate.Profiles).ProfileId);
        Assert.Contains("사용자 캐시 무시", duplicate.Source);
    }

    [Fact]
    public void CacheInvalidatesWhenEitherProfileFileChanges()
    {
        using var files = new ProfileFiles();
        files.WriteBundled(Profile("3.0.0.24268", 1, "BUNDLED-ONE"));
        files.WriteUser(Profile("3.0.0.24267", 1, "USER-ONE"));

        var first = files.Repository.GetProfiles();
        files.WriteBundled(Profile("3.0.0.24268", 2, "BUNDLED-TWO-CHANGED"));
        var afterBundledChange = files.Repository.GetProfiles();
        files.WriteUser(Profile("3.0.0.24267", 2, "USER-TWO-CHANGED"));
        var afterUserChange = files.Repository.GetProfiles();

        Assert.True(afterBundledChange.Generation > first.Generation);
        Assert.True(afterUserChange.Generation > afterBundledChange.Generation);
        Assert.Equal("BUNDLED-TWO-CHANGED", afterBundledChange.Profiles.Single(x => x.FileVersion == "3.0.0.24268").ProfileId);
        Assert.Equal("USER-TWO-CHANGED", afterUserChange.Profiles.Single(x => x.FileVersion == "3.0.0.24267").ProfileId);
    }

    private static string Profile(string version, int revision, string id, char hashCharacter = 'A') => $$"""
        [
          {
            "profileSchemaVersion": 1,
            "profileId": "{{id}}",
            "profileRevision": {{revision}},
            "fileVersion": "{{version}}",
            "moduleName": "Warcraft III.exe",
            "enabled": true,
            "verified": true,
            "sha256": "{{new string(hashCharacter, 64)}}",
            "locatorKind": "ModuleOffset",
            "moduleOffset": 0
          }
        ]
        """;

    private sealed class ProfileFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "orand-profile-merge-" + Guid.NewGuid().ToString("N"));
        public string BundledPath { get; }
        public string UserPath { get; }
        public MemoryProfileRepository Repository { get; }

        public ProfileFiles()
        {
            Directory.CreateDirectory(_root);
            BundledPath = Path.Combine(_root, "bundled-memory-profiles.json");
            UserPath = Path.Combine(_root, "user-memory-profiles.json");
            Repository = new MemoryProfileRepository(UserPath, BundledPath);
        }

        public void WriteBundled(params string[] profiles) => File.WriteAllText(BundledPath, Combine(profiles));
        public void WriteUser(params string[] profiles) => File.WriteAllText(UserPath, Combine(profiles));

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private static string Combine(IReadOnlyList<string> profiles) => profiles.Count == 1
            ? profiles[0]
            : "[" + string.Join(",", profiles.Select(profile => profile.Trim()[1..^1])) + "]";
    }
}
