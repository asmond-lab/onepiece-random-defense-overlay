using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

public sealed record MapSourceManifestMember(
    string Identifier,
    string Path,
    string Purpose,
    string Sha256);

public sealed record MapSourceManifest(
    string MapVersion,
    ImmutableArray<MapSourceManifestMember> Members);

public static class MapSourceManifestVerifier
{
    public const string FileName = "map-source-manifest-2314.json";

    private static readonly ImmutableArray<MapSourceManifestMember> ExpectedMembers =
    [
        new("source-metadata", "Data/map-source-metadata-2314.json", "Pins the 2.314 map archive and extracted source members.", "2d262ff929fcae3d70a94c6c76969708f37f28a62b9dd06b52b0af80d4c10c82"),
        new("story", "Data/story-progression-2314.json", "Defines source-pinned story stages, owners, and rewards.", "8c24aff6b74ebc6a4117c9aa57bfc236cbd286d12ad85faf802c6453dc58ae0b"),
        new("navigation", "Data/navigation-mechanics-2314.json", "Defines source-pinned navigation mechanics and bounded scenarios.", "cbd13f3b7b675906f8b1966319c6e8d05880159beb18e63bf2e7828fa1904790"),
        new("recipe-overrides", "Data/map-recipe-overrides-2314.txt", "Pins map-authored recipe overrides used by route allocation.", "7f738b496d0e361c4e742dea80e20b523a29d5681c6c428e38d9bdfda947a406"),
        new("combine-commands", "Data/map-combine-commands-2314.txt", "Pins map-authored combine commands used by route allocation.", "0af03328afd3c1bbb59bd49dc1ed0651e94568b87de3486ab265533ded2a6d7d")
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static MapSourceManifest VerifyDirectory(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var manifestPath = Path.Combine(root, "Data", FileName);
        MapSourceManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<MapSourceManifest>(
                File.ReadAllBytes(manifestPath), JsonOptions)
                ?? throw new InvalidDataException("Map source manifest is empty.");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidDataException("Map source manifest could not be read.", exception);
        }

        if (!string.Equals(manifest.MapVersion, "2.314", StringComparison.Ordinal))
            throw new InvalidDataException("Map source manifest version is not 2.314.");
        if (manifest.Members.Length != ExpectedMembers.Length)
            throw new InvalidDataException("Map source manifest must contain exactly five members.");
        if (manifest.Members.Select(member => member.Identifier)
            .Distinct(StringComparer.Ordinal).Count() != ExpectedMembers.Length)
            throw new InvalidDataException("Map source manifest identifiers must be unique.");

        for (var index = 0; index < ExpectedMembers.Length; index++)
        {
            var expected = ExpectedMembers[index];
            var actual = manifest.Members[index];
            if (actual != expected)
                throw new InvalidDataException($"Map source manifest member {index} is not the pinned member.");
            var memberPath = Path.GetFullPath(Path.Combine(root,
                actual.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!memberPath.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Map source manifest member escapes the repository root.");
            if (!File.Exists(memberPath))
                throw new InvalidDataException($"Map source manifest member is missing: {actual.Identifier}.");
            var actualHash = CanonicalTextSha256(memberPath);
            if (!string.Equals(actualHash, actual.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Map source manifest hash mismatch: {actual.Identifier}.");
        }

        return manifest;
    }

    private static string CanonicalTextSha256(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var firstCarriageReturn = Array.IndexOf(bytes, (byte)'\r');
        if (firstCarriageReturn < 0)
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var canonical = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, canonical, 0, firstCarriageReturn);
        var written = firstCarriageReturn;
        for (var index = firstCarriageReturn; index < bytes.Length; index++)
        {
            if (bytes[index] == (byte)'\r')
            {
                canonical[written++] = (byte)'\n';
                if (index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n')
                    index++;
            }
            else
            {
                canonical[written++] = bytes[index];
            }
        }
        return Convert.ToHexString(SHA256.HashData(canonical.AsSpan(0, written)))
            .ToLowerInvariant();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeSignalStatus>))]
public enum RuntimeSignalStatus { Unproven, Proven, SourceBound }

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeSignalDisposition>))]
public enum RuntimeSignalDisposition { DisableAdaptivePlanning, Enabled, UnknownRouted }

public sealed record RuntimeSignalEvidence(
    string Derivation,
    int WidthBits,
    string BeforeValue,
    string AfterValue,
    string VisibleTrigger,
    string RuntimePathField,
    string ActualArchivePath,
    string ActualArchiveSha256,
    long BytesRead,
    int Calls,
    string ResetBehavior,
    string Confidence);

public sealed record RuntimeSignalFeasibility(
    string Identifier,
    RuntimeSignalStatus Status,
    RuntimeSignalDisposition Disposition,
    ImmutableArray<string> FiniteScenarios,
    RuntimeSignalEvidence Evidence);

public sealed record RuntimeSignalFeasibilityProfile(
    string MapVersion,
    bool AdaptivePlanningCapable,
    bool LiveReadinessRequiresCurrentMapIdentityProof,
    int PrimarySliceBytes,
    bool SideChannelCountersSeparateFromPrimarySlice,
    ImmutableArray<RuntimeSignalFeasibility> MandatorySignals,
    ImmutableArray<RuntimeSignalFeasibility> OptionalSignals);

public static class RuntimeSignalFeasibilityProfileLoader
{
    public const string FileName = "runtime-signal-feasibility-2314.json";

    private static readonly ImmutableArray<string> MandatoryIdentifiers =
        ["actual-map-archive-sha256"];
    private static readonly ImmutableArray<string> OptionalIdentifiers =
    [
        "resources", "random-wisp", "helper", "exact-top-count", "isekai",
        "rerolls-gamble-bounty-boss-item", "selected-top-cooldown", "current-wave",
        "remaining-combat-horizon"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static RuntimeSignalFeasibilityProfile LoadFromDirectory(string dataDirectory)
    {
        RuntimeSignalFeasibilityProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<RuntimeSignalFeasibilityProfile>(
                File.ReadAllBytes(Path.Combine(dataDirectory, FileName)), JsonOptions)
                ?? throw new InvalidDataException("Runtime signal profile is empty.");
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            throw new InvalidDataException("Runtime signal profile could not be read.", exception);
        }

        if (profile.MapVersion != "2.314" || profile.PrimarySliceBytes != 4 * 1024 * 1024)
            throw new InvalidDataException("Runtime signal profile identity or primary slice is invalid.");
        if (!profile.SideChannelCountersSeparateFromPrimarySlice)
            throw new InvalidDataException("Side-channel counters must stay outside the primary slice.");
        if (!profile.AdaptivePlanningCapable ||
            !profile.LiveReadinessRequiresCurrentMapIdentityProof)
            throw new InvalidDataException(
                "Adaptive planning capability must require current map identity proof.");
        ValidateOrder(profile.MandatorySignals, MandatoryIdentifiers, "mandatory");
        ValidateOrder(profile.OptionalSignals, OptionalIdentifiers, "optional");

        foreach (var signal in profile.MandatorySignals)
        {
            if (signal.Status == RuntimeSignalStatus.Proven)
            {
                ValidateProvenMandatory(signal);
                if (signal.Disposition != RuntimeSignalDisposition.Enabled)
                    throw new InvalidDataException($"Proven mandatory signal is not enabled: {signal.Identifier}.");
            }
            else if (signal.Status != RuntimeSignalStatus.Unproven ||
                     signal.Disposition != RuntimeSignalDisposition.DisableAdaptivePlanning)
                throw new InvalidDataException($"Unproven mandatory signal must disable adaptive planning: {signal.Identifier}.");
        }

        foreach (var signal in profile.OptionalSignals)
            if (signal.Status != RuntimeSignalStatus.SourceBound ||
                signal.Disposition != RuntimeSignalDisposition.UnknownRouted ||
                signal.FiniteScenarios.IsDefaultOrEmpty || signal.Evidence.WidthBits <= 0 ||
                string.IsNullOrWhiteSpace(signal.Evidence.Derivation))
                throw new InvalidDataException($"Optional signal is not finitely classified: {signal.Identifier}.");

        return profile;
    }

    private static void ValidateOrder(
        ImmutableArray<RuntimeSignalFeasibility> signals,
        ImmutableArray<string> expected,
        string kind)
    {
        if (signals.IsDefault || !signals.Select(signal => signal.Identifier)
            .SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException($"Runtime {kind} signal set is not exact and ordered.");
    }

    private static void ValidateProvenMandatory(RuntimeSignalFeasibility signal)
    {
        var evidence = signal.Evidence;
        if (evidence.Calls <= 0 || evidence.BytesRead <= 0 ||
            string.IsNullOrWhiteSpace(evidence.VisibleTrigger) ||
            string.IsNullOrWhiteSpace(evidence.ResetBehavior) ||
            string.IsNullOrWhiteSpace(evidence.Confidence))
            throw new InvalidDataException($"Mandatory signal proof is incomplete: {signal.Identifier}.");
        if (signal.Identifier == "actual-map-archive-sha256" &&
            (string.IsNullOrWhiteSpace(evidence.RuntimePathField) ||
             string.IsNullOrWhiteSpace(evidence.ActualArchivePath) ||
             evidence.ActualArchiveSha256.Length != 64 ||
             evidence.ActualArchiveSha256.Any(character => !Uri.IsHexDigit(character))))
            throw new InvalidDataException("Actual map archive identity proof is incomplete.");
    }
}
