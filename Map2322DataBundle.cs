using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

public sealed record Map2322BundleMember(string Identifier, string File, int NormalizedBytes, string Sha256);
public sealed record Map2322Hotkey(string Result, string Ability, string Key, IReadOnlyList<string> HostRawcodes);
public sealed record Map2322BundleManifest(int SchemaVersion, string MapVersion, string ArchiveSha256, ImmutableArray<Map2322BundleMember> Members);

/// <summary>Closed offline 2.322 data; loading never approves live recognition or automatic crafting.</summary>
public sealed class Map2322DataBundle
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly ImmutableArray<Map2322BundleMember> Approved =
    [
        new("map-source-metadata", "map-source-metadata-2322.json", 2469, "7d6fcf67258ba48eb98fc5e2a00031115bc242071f54cc9c94cd2188e99f9a0d"),
        new("map-recipes", "map-recipes-2322.json", 254428, "a0627fe401fe22694eaffe9c101598c7a7cf32a8990bc286fafd876de499b71a"),
        new("navigation-mechanics", "navigation-mechanics-2322.json", 52233, "a56546fc1ebceaccdd3e03b4df03b27b47f9f518a8568eb48d102e181d1cf040"),
        new("story-progression", "story-progression-2322.json", 57371, "0a9201124fbc317ad19d133b0e2a564d71358c105d085f29913c50a9f375d4cf"),
        new("map-mechanics", "map-mechanics-2322.json", 1795, "8fee1b621f582047746be02a82b592ecf937bd2d3794aa1da199302397a33a41"),
        new("commands", "map-combine-commands-2322.txt", 2839, "7553ff537ef4cab91182845db0bd8faa58bb2b87c63b332fe4d5d2ff1b158011"),
        new("map-combine-hotkeys", "map-combine-hotkeys-2322.json", 61345, "2b8f6882e91e709bf9bd33a05331071dbb1f0290cd49006fecd89f921a1063cc")
    ];
    private static readonly ImmutableArray<Map2322BundleMember> Approved2323 =
    [
        new("map-source-metadata", "map-source-metadata-2323.json", 1887, "9be39d3b12f48482cf0f86be2216a5c5a5829592fe1e01baf9232c3801372bdf"),
        new("map-recipes", "map-recipes-2323.json", 254426, "e37e4e038888790dfe0f01d055521465057dea097a37462982ca19a8b6e70d20"),
        new("navigation-mechanics", "navigation-mechanics-2323.json", 52218, "d9e5204a41f3565267b8b83b35c358a4f592978893d5b4a242f641606859d013"),
        new("story-progression", "story-progression-2323.json", 58796, "9d9ad5ca99bf87aa3c697c74e99e01f46418259ca1527fa512d6b2d3b825bd5e"),
        new("commands", "commands-2323.txt", 2839, "9621fb7e3920693c6b14f07f36cf83a1bdd9b98b2825c2d4436c11c4450e42d0"),
        new("map-combine-hotkeys", "map-combine-hotkeys-2323.json", 61345, "0bf7849015a1ce7b0710f76099161909aad115002ebf026ef3bc5b5315288308"),
        new("map-mechanics", "map-mechanics-2323.json", 1793, "da4fe8f154c14bd81fced621d14cec91357b0aed30b8ad2c45f80b837dd7a61a")
    ];
    public static ImmutableArray<Map2322BundleMember> ExpectedMembers => Approved;
    public static ImmutableArray<Map2322BundleMember> Expected2323Members => Approved2323;
    public string MapVersion { get; }
    public Map2322RecipeRegistry Recipes { get; }
    public Map2322NavigationProfile Navigation { get; }
    public Map2322StoryProfile Story { get; }
    public MapSourceMetadata Source { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Commands { get; }
    public IReadOnlyList<Map2322Hotkey> Hotkeys { get; }
    public string Fingerprint { get; }

    private Map2322DataBundle(Dictionary<string, byte[]> verified, string mapVersion, ImmutableArray<Map2322BundleMember> approved)
    {
        MapVersion = mapVersion;
        var script = mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.JassSha256 : Map2322SourceContract.JassSha256;
        Recipes = Map2322RecipeRegistry.Load(verified["map-recipes"], mapVersion);
        Navigation = new Map2322NavigationProfile(verified["navigation-mechanics"], mapVersion);
        Story = new Map2322StoryProfile(verified["story-progression"], mapVersion);
        Commands = Utf8.GetString(verified["commands"]).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#')).Select(line => line.Split('=', 2))
            .ToDictionary(row => row[0], row => (IReadOnlyList<string>)row[1].Split('|'), StringComparer.Ordinal);
        using var hotkeyDoc = JsonDocument.Parse(verified["map-combine-hotkeys"]);
        CheckIdentity(hotkeyDoc.RootElement, mapVersion);
        if (hotkeyDoc.RootElement.GetProperty("sourceSha256").GetString() != script)
            throw new InvalidDataException("Hotkey source mismatch.");
        Hotkeys = hotkeyDoc.RootElement.GetProperty("entries").EnumerateArray().Select(x => new Map2322Hotkey(
            x.GetProperty("result").GetString()!, x.GetProperty("ability").GetString()!, x.GetProperty("key").GetString()!,
            x.GetProperty("hosts").EnumerateArray().Select(host => host.GetProperty("rawcode").GetString()!).ToArray())).ToArray();
        if (Commands.Count != 81 || Commands.Values.Sum(aliases => aliases.Count) != 162 || Hotkeys.Count != 156 ||
            Hotkeys.Any(x => x.HostRawcodes.Count == 0 || x.Key.Length != 1))
            throw new InvalidDataException("Incomplete 2.322 combination registrations.");
        using var source = JsonDocument.Parse(verified["map-source-metadata"]);
        CheckIdentity(source.RootElement, mapVersion);
        if (source.RootElement.GetProperty("archive").GetProperty("archiveSha256").GetString() != (mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.ArchiveSha256 : Map2322SourceContract.ArchiveSha256) ||
            source.RootElement.GetProperty("archive").GetProperty("archiveBytes").GetInt64() != (mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.ArchiveLengthBytes : Map2322SourceContract.ArchiveLengthBytes) ||
            source.RootElement.GetProperty("members")[0].GetProperty("sha256").GetString() != script)
            throw new InvalidDataException("Wrong extracted source identity.");
        var archive = source.RootElement.GetProperty("archive");
        Source = new MapSourceMetadata(mapVersion,
            new MapArchivePin(archive.GetProperty("archive").GetString()!, archive.GetProperty("archiveBytes").GetInt64(),
                archive.GetProperty("archiveSha256").GetString()!),
            source.RootElement.GetProperty("members").EnumerateArray().Select(member => new SourceMemberPin(
                member.GetProperty("name").GetString()!, member.GetProperty("bytes").GetInt64(),
                member.GetProperty("sha256").GetString()!)).ToImmutableArray(),
            source.RootElement.GetProperty("sourceRefs").EnumerateArray().Select(pin => new SourcePin(
                "war3map.j", pin.GetProperty("startLine").GetInt32(), pin.GetProperty("endLine").GetInt32(),
                "Pinned " + mapVersion + " source excerpt " + pin.GetProperty("sha256").GetString())).ToImmutableArray(),
            // The 2.322 metadata pins member bytes, not parsed object/text records. Empty evidence is intentional.
            new W3uEvidence("war3map.w3u", new W3uParserLimits(0, 0, 0, 0, 0),
                new W3uLayoutEvidence(0, 0, [], 0, 0, 0, 0, 0), []),
            new WtsEvidence("war3map.wts", "", 0, []));
        using var mechanics = JsonDocument.Parse(verified["map-mechanics"]);
        CheckIdentity(mechanics.RootElement, mapVersion);
        if (mechanics.RootElement.GetProperty("sourceSha256").GetString() != script)
            throw new InvalidDataException("Wrong mechanics source.");
        Fingerprint = Hash(Utf8.GetBytes(string.Join("|", approved.Select(x => x.Sha256))));
    }

    public static Map2322DataBundle LoadBundled(string mapVersion = "2.322") => LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"), mapVersion);
    public static Map2322DataBundle LoadFromDirectory(string directory, string mapVersion = "2.322")
    {
        if (mapVersion is not ("2.322" or "2.323")) throw new InvalidDataException("Unsupported map bundle.");
        var approved = mapVersion == Map2323SourceContract.MapVersion ? Approved2323 : Approved;
        var root = Path.GetFullPath(directory);
        try
        {
            using var manifestDoc = JsonDocument.Parse(ReadBounded(Path.Combine(root, "map-source-manifest-" + mapVersion.Replace(".", "") + ".json"), 16 * 1024));
            RejectDuplicates(manifestDoc.RootElement);
            var manifest = JsonSerializer.Deserialize<Map2322BundleManifest>(manifestDoc.RootElement, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("Empty manifest.");
            if (manifest.SchemaVersion != 1 || manifest.MapVersion != mapVersion ||
                manifest.ArchiveSha256 != (mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.ArchiveSha256 : Map2322SourceContract.ArchiveSha256) || manifest.Members.IsDefault || !manifest.Members.SequenceEqual(approved))
                throw new InvalidDataException("Mixed or unapproved 2.322 manifest.");
            var verified = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in approved)
            {
                var bytes = ReadBounded(Path.Combine(root, entry.File), 2 * 1024 * 1024);
                bytes = Utf8.GetBytes(Utf8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
                if (bytes.Length != entry.NormalizedBytes || Hash(bytes) != entry.Sha256)
                    throw new InvalidDataException("2.322 member integrity failure: " + entry.Identifier);
                verified.Add(entry.Identifier, bytes);
            }
            return new Map2322DataBundle(verified, mapVersion, approved);
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid 2.322 JSON.", ex); }
    }
    internal static void CheckIdentity(JsonElement root, string mapVersion = "2.322")
    {
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("mapVersion").GetString() != mapVersion)
            throw new InvalidDataException("Mixed 2.322 member version.");
    }
    private static byte[] ReadBounded(string path, int maximum)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Length <= 0 || info.Length > maximum) throw new InvalidDataException("2.322 file bound or link.");
        using var stream = File.OpenRead(path);
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("2.322 file changed during read.");
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate manifest property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicates(value);
    }
}
