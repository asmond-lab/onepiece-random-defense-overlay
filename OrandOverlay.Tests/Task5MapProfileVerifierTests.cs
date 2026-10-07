using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Task5MapProfileVerifierTests
{
    private static readonly (string Id, string Path, string Purpose, string Sha256)[] ExpectedMembers =
    [
        ("source-metadata", "Data/map-source-metadata-2314.json", "Pins the 2.314 map archive and extracted source members.", "2d262ff929fcae3d70a94c6c76969708f37f28a62b9dd06b52b0af80d4c10c82"),
        ("story", "Data/story-progression-2314.json", "Defines source-pinned story stages, owners, and rewards.", "8c24aff6b74ebc6a4117c9aa57bfc236cbd286d12ad85faf802c6453dc58ae0b"),
        ("navigation", "Data/navigation-mechanics-2314.json", "Defines source-pinned navigation mechanics and bounded scenarios.", "cbd13f3b7b675906f8b1966319c6e8d05880159beb18e63bf2e7828fa1904790"),
        ("recipe-overrides", "Data/map-recipe-overrides-2314.txt", "Pins map-authored recipe overrides used by route allocation.", "7f738b496d0e361c4e742dea80e20b523a29d5681c6c428e38d9bdfda947a406"),
        ("combine-commands", "Data/map-combine-commands-2314.txt", "Pins map-authored combine commands used by route allocation.", "0af03328afd3c1bbb59bd49dc1ed0651e94568b87de3486ab265533ded2a6d7d")
    ];

    private static readonly string[] ExpectedOptionalSignals =
    [
        "resources", "random-wisp", "helper", "exact-top-count", "isekai",
        "rerolls-gamble-bounty-boss-item", "selected-top-cooldown", "current-wave",
        "remaining-combat-horizon"
    ];

    [Fact]
    public void NavigationSelectionIsNotAReadinessSignalOrConsumerInput()
    {
        var profile = RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(
            Path.Combine(RepositoryRoot(), "Data"));

        Assert.Equal(["actual-map-archive-sha256"],
            profile.MandatorySignals.Select(signal => signal.Identifier));
        Assert.DoesNotContain(profile.OptionalSignals,
            signal => signal.Identifier == "navigation-selection");
    }

    [Fact]
    public void ProvenMapIdentityAloneEnablesPlanningWithoutNavigationSelection()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"orand-task5-map-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(RepositoryRoot(), "Data",
                RuntimeSignalFeasibilityProfileLoader.FileName);
            var destination = Path.Combine(directory,
                RuntimeSignalFeasibilityProfileLoader.FileName);
            File.Copy(source, destination);
            var json = JsonNode.Parse(File.ReadAllText(destination))!.AsObject();
            var identity = json["MandatorySignals"]!.AsArray()[0]!.AsObject();
            identity["Status"] = "Proven";
            identity["Disposition"] = "Enabled";
            identity["Evidence"]!["ActualArchivePath"] =
                "runtime-resolved/ORDR_S2_2.314[R].w3x";
            File.WriteAllText(destination, json.ToJsonString());

            var profile = RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(directory);

            Assert.True(profile.AdaptivePlanningCapable);
            Assert.True(profile.LiveReadinessRequiresCurrentMapIdentityProof);
            Assert.Single(profile.MandatorySignals);
            Assert.Equal("actual-map-archive-sha256",
                profile.MandatorySignals[0].Identifier);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ShippedClosedManifestHasExactOrderedMembersAndCurrentHashes()
    {
        var root = RepositoryRoot();
        var manifest = MapSourceManifestVerifier.VerifyDirectory(root);

        Assert.Equal("2.314", manifest.MapVersion);
        Assert.Equal(ExpectedMembers.Length, manifest.Members.Length);
        for (var index = 0; index < ExpectedMembers.Length; index++)
        {
            var expected = ExpectedMembers[index];
            var actual = manifest.Members[index];
            Assert.Equal((expected.Id, expected.Path, expected.Purpose, expected.Sha256),
                (actual.Identifier, actual.Path, actual.Purpose, actual.Sha256));
            Assert.Equal(expected.Sha256, CanonicalTextSha256(Path.Combine(root,
                expected.Path.Replace('/', Path.DirectorySeparatorChar))));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("reordered")]
    [InlineData("path-confused")]
    [InlineData("hash-mismatch")]
    [InlineData("member-byte-mismatch")]
    public void ClosedManifestFailsForEveryShapeOrContentMutation(string mutation)
    {
        using var copy = ManifestCopy.Create();
        copy.Mutate(mutation);

        Assert.Throws<InvalidDataException>(() =>
            MapSourceManifestVerifier.VerifyDirectory(copy.Root));
    }

    [Fact]
    public void ShippedRuntimeProfileIsCapableButRequiresLiveMapIdentity()
    {
        var profile = RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(
            Path.Combine(RepositoryRoot(), "Data"));

        Assert.True(profile.AdaptivePlanningCapable);
        Assert.True(profile.LiveReadinessRequiresCurrentMapIdentityProof);
        Assert.Equal(4 * 1024 * 1024, profile.PrimarySliceBytes);
        Assert.Equal(["actual-map-archive-sha256"],
            profile.MandatorySignals.Select(signal => signal.Identifier));
        Assert.All(profile.MandatorySignals, signal =>
        {
            Assert.Equal(RuntimeSignalStatus.Unproven, signal.Status);
            Assert.Equal(RuntimeSignalDisposition.DisableAdaptivePlanning,
                signal.Disposition);
        });
        Assert.Equal(ExpectedOptionalSignals,
            profile.OptionalSignals.Select(signal => signal.Identifier));
        Assert.All(profile.OptionalSignals, signal =>
        {
            Assert.Equal(RuntimeSignalStatus.SourceBound, signal.Status);
            Assert.Equal(RuntimeSignalDisposition.UnknownRouted, signal.Disposition);
            Assert.NotEmpty(signal.FiniteScenarios);
        });
        Assert.True(profile.SideChannelCountersSeparateFromPrimarySlice);
    }

    [Theory]
    [InlineData("disable-capability")]
    [InlineData("missing-optional")]
    [InlineData("unclassified-optional")]
    [InlineData("empty-bound")]
    [InlineData("side-channel-in-slice")]
    [InlineData("navigation-as-mandatory")]
    public void FeasibilityProfileFailsClosedWhenEvidenceOrClassificationIsIncomplete(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"orand-task5-signals-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(RepositoryRoot(), "Data", RuntimeSignalFeasibilityProfileLoader.FileName);
            var destination = Path.Combine(directory, RuntimeSignalFeasibilityProfileLoader.FileName);
            File.Copy(source, destination);
            var json = JsonNode.Parse(File.ReadAllText(destination))!.AsObject();
            MutateSignals(json, mutation);
            File.WriteAllText(destination, json.ToJsonString());

            Assert.Throws<InvalidDataException>(() =>
                RuntimeSignalFeasibilityProfileLoader.LoadFromDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void MutateSignals(JsonObject json, string mutation)
    {
        var mandatory = json["MandatorySignals"]!.AsArray();
        var optional = json["OptionalSignals"]!.AsArray();
        switch (mutation)
        {
            case "disable-capability": json["AdaptivePlanningCapable"] = false; break;
            case "missing-optional": optional.RemoveAt(0); break;
            case "unclassified-optional": optional[0]!["Disposition"] = "DisableAdaptivePlanning"; break;
            case "empty-bound": optional[0]!["FiniteScenarios"] = new JsonArray(); break;
            case "side-channel-in-slice": json["SideChannelCountersSeparateFromPrimarySlice"] = false; break;
            case "navigation-as-mandatory":
                var navigation = mandatory[0]!.DeepClone();
                navigation["Identifier"] = "navigation-selection";
                mandatory.Add(navigation);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../"));

    private static string CanonicalTextSha256(string path)
    {
        var canonical = File.ReadAllText(path)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private sealed class ManifestCopy : IDisposable
    {
        public string Root { get; }

        private ManifestCopy(string root) => Root = root;

        public static ManifestCopy Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"orand-task5-manifest-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            var repository = RepositoryRoot();
            foreach (var member in ExpectedMembers)
            {
                var relative = member.Path.Replace('/', Path.DirectorySeparatorChar);
                File.Copy(Path.Combine(repository, relative), Path.Combine(root, relative));
            }
            File.Copy(Path.Combine(repository, "Data", MapSourceManifestVerifier.FileName),
                Path.Combine(root, "Data", MapSourceManifestVerifier.FileName));
            return new ManifestCopy(root);
        }

        public void Mutate(string mutation)
        {
            var path = Path.Combine(Root, "Data", MapSourceManifestVerifier.FileName);
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var members = json["Members"]!.AsArray();
            switch (mutation)
            {
                case "missing": members.RemoveAt(0); break;
                case "extra": members.Add(members[0]!.DeepClone()); break;
                case "duplicate": members[1] = members[0]!.DeepClone(); break;
                case "reordered":
                    var first = members[0]!.DeepClone();
                    var second = members[1]!.DeepClone();
                    members[0] = second;
                    members[1] = first;
                    break;
                case "path-confused": members[0]!["Path"] = ExpectedMembers[1].Path; break;
                case "hash-mismatch": members[0]!["Sha256"] = new string('0', 64); break;
                case "member-byte-mismatch": File.AppendAllText(Path.Combine(Root,
                    ExpectedMembers[0].Path.Replace('/', Path.DirectorySeparatorChar)), " "); break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            File.WriteAllText(path, json.ToJsonString());
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
