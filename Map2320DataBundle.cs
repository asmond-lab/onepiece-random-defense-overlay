using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

/// <summary>A closed offline dataset. Its acceptance never approves an executable or native reader.</summary>
public sealed class Map2320DataBundle
{
    public const string Version = "2.320";
    public const string PlayVersion = "2.321";
    public static bool IsCompatible(string mapVersion) => mapVersion is Version or PlayVersion;
    public const string ManifestFile = "map-source-manifest-2320.json";
    public const string ArchiveSha256 = "68445631fdaca12a343e9465e5bc5dc9b4c8c6cc927d701f52e2cb822821e15f";
    public const string CommandSha256 = "7680de32737bd404a7f88f282d69be444c959767e09b32cd44b11fe80ff3ffd0";
    public const string NikaSha256 = "32b8e35e7bd90bed6265ccb83b862ff47999495dfc78fe36ec2c7fe4b45186ef";
    public const string DisplayLabel = "ORDR 2.320 오프라인 조합·스토리·항법·보드 · 실시간 인식 미검증";
    public bool LiveRecognitionSupported => false;
    public bool AutomaticNavigationScoringSupported => false;
    public MapSourceMetadata Source { get; }
    public Map2320StoryProfile Story { get; }
    public Map2320NavigationData Navigation { get; }
    public Map2320RecipeRegistry Recipes { get; }
    public MapRecipeMechanics Nika { get; }
    public Map2320UtilityBoard UtilityBoard { get; }
    public string Commands { get; }
    public string Fingerprint { get; }

    private static readonly ImmutableArray<Map2320BundleMember> Approved =
    [
        new("source", "map-source-metadata-2320.json", 51738, Map2320SourceMetadata.DataSha256),
        new("story", "story-progression-2320.json", 494902, Map2320StoryProfile.DataSha256),
        new("navigation", "navigation-mechanics-2320.json", 84238, Map2320NavigationMechanics.DataSha256),
        new("recipes", "map-recipes-2320.json", 1053769, Map2320RecipeRegistry.DataSha256),
        new("conditions", "map-mechanics-2320.json", 392, NikaSha256),
        new("commands", "map-combine-commands-2320.txt", 2911, CommandSha256),
        new("board", "utility-board-2320.json", 26607, Map2320UtilityBoard.DatasetSha256.ToLowerInvariant())
    ];
    public static ImmutableArray<Map2320BundleMember> ExpectedMembers => Approved;

    private Map2320DataBundle(IReadOnlyDictionary<string, byte[]> bytes)
    {
        Source = Map2320SourceMetadata.Load(bytes["source"]);
        Story = Map2320StoryProfile.Load(bytes["story"]);
        Navigation = Map2320NavigationMechanics.Load(bytes["navigation"]);
        Recipes = Map2320RecipeRegistry.Load(bytes["recipes"]);
        Nika = MapRecipeMechanics.Load(bytes["conditions"]);
        UtilityBoard = Map2320UtilityBoard.Load(bytes["board"]);
        Commands = StrictUtf8.GetString(bytes["commands"]);
        Fingerprint = Hash(Encoding.UTF8.GetBytes(string.Join("|", Approved.Select(x => x.Sha256))));
    }
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12
    };
    public static Map2320DataBundle LoadBundled() => LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
    public static Map2320DataBundle LoadFromDirectory(string dataDirectory)
    {
        var root = Path.GetFullPath(dataDirectory);
        var manifestBytes = ReadBounded(Path.Combine(root, ManifestFile), 16 * 1024);
        Map2320BundleManifest manifest;
        try
        {
            using var document = JsonDocument.Parse(manifestBytes);
            RejectDuplicates(document.RootElement);
            manifest = JsonSerializer.Deserialize<Map2320BundleManifest>(manifestBytes, Json)
                ?? throw new InvalidDataException("2.320 manifest missing.");
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid 2.320 manifest.", e); }
        if (manifest.SchemaVersion != 1 || manifest.MapVersion != Version || manifest.ArchiveSha256 != ArchiveSha256 ||
            manifest.Members.IsDefault || !manifest.Members.SequenceEqual(Approved))
            throw new InvalidDataException("Mixed or unapproved 2.320 dataset contract.");
        var verified = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in Approved)
        {
            // Paths come exclusively from the compiled contract, never from untrusted manifest entries.
            var bytes = Normalize(ReadBounded(Path.Combine(root, entry.File), 2 * 1024 * 1024));
            if (bytes.Length != entry.NormalizedBytes || Hash(bytes) != entry.Sha256)
                throw new InvalidDataException("2.320 dataset integrity failure: " + entry.Identifier);
            verified.Add(entry.Identifier, bytes);
        }
        // Parsers receive exactly the accepted bytes; no second file read or fallback to legacy files.
        return new Map2320DataBundle(verified);
    }
    private static byte[] ReadBounded(string file, int maximum)
    {
        var info = new FileInfo(file);
        if (info.LinkTarget is not null) throw new InvalidDataException("Dataset links are not accepted.");
        using var stream = File.OpenRead(file);
        if (stream.Length <= 0 || stream.Length > maximum) throw new InvalidDataException("Dataset size bound.");
        var result = new byte[(int)stream.Length];
        stream.ReadExactly(result);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Dataset changed during read.");
        return result;
    }
    internal static byte[] Normalize(byte[] bytes) => StrictUtf8.GetBytes(StrictUtf8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate manifest field.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
public sealed record Map2320BundleMember(string Identifier, string File, int NormalizedBytes, string Sha256);
public sealed record Map2320BundleManifest(int SchemaVersion, string MapVersion, string ArchiveSha256, ImmutableArray<Map2320BundleMember> Members);
