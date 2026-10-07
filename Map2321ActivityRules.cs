using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrandOverlay;

internal sealed record ActivityRecipeRule(string Id, string OutputRawcode, int OutputCount,
    ImmutableDictionary<string, int> Ingredients, ImmutableArray<JsonElement> OtherRequirements, int SourceLine);
internal sealed record ActivityWispRule(string Id, string Rawcode, string Kind,
    ImmutableHashSet<string> Outputs, string? Counter, JsonElement SourceFunctions);
internal sealed record ActivityGambleRule(string Id, string? Attempts, string? Successes, string? Failures,
    ImmutableArray<string> Branches, ImmutableHashSet<string> Outputs, JsonElement SourceFunctions);

internal sealed class Map2321ActivityRules
{
    internal const string FileName = "map-activity-2321.json";
    internal const string SourceSha256 = "55d0ffb9921433f45a9244cb946bdd27dcd2552a3550d30c4617c2eaccb94e97";
    internal const string DataSha256 = "86579af687b9960e08ac3302b8ab867830d2028a810beccc4a1ca83c2586e6aa";
    internal string MapVersion { get; }
    internal ImmutableHashSet<string> IntegerArrays { get; }
    internal ImmutableArray<ActivityRecipeRule> Recipes { get; }
    internal ImmutableArray<ActivityWispRule> Wisps { get; }
    internal ImmutableArray<ActivityGambleRule> Gambles { get; }

    private Map2321ActivityRules(JsonElement root)
    {
        MapVersion = root.GetProperty("mapVersion").GetString()!;
        IntegerArrays = root.GetProperty("integerArrays").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!).ToImmutableHashSet(StringComparer.Ordinal);
        var declarations = Map2320GrowthSource.LoadBundled(MapVersion).Globals;
        if (IntegerArrays.Count != (MapVersion is "2.322" or "2.323" ? 18 : 19) || IntegerArrays.Any(name => declarations.GetValueOrDefault(name) != 9))
            throw new InvalidDataException("Activity counter declaration mismatch.");
        Recipes = root.GetProperty("recipes").EnumerateArray()
            .Where(row => row.GetProperty("isActiveChoice").GetBoolean())
            .Select(row => new ActivityRecipeRule(
                row.GetProperty("id").GetString()!, row.GetProperty("outputRawcode").GetString()!,
                row.GetProperty("outputCount").GetInt32(),
                row.GetProperty("ingredients").EnumerateObject().ToImmutableDictionary(
                    term => term.Name, term => term.Value.GetInt32(), StringComparer.Ordinal),
                row.GetProperty("otherRequirements").EnumerateArray().Select(term => term.Clone()).ToImmutableArray(),
                row.GetProperty("sourceLine").GetInt32())).ToImmutableArray();
        Wisps = root.GetProperty("wisps").EnumerateArray().Select(row => new ActivityWispRule(
            row.GetProperty("id").GetString()!, row.GetProperty("rawcode").GetString()!,
            row.GetProperty("kind").GetString()!,
            row.GetProperty("outputRawcodes").EnumerateArray().Select(item => item.GetString()!).ToImmutableHashSet(StringComparer.Ordinal),
            row.TryGetProperty("consumptionCounterGlobal", out var counter) ? counter.GetString() : null,
            row.GetProperty("sourceFunctions").Clone())).ToImmutableArray();
        Gambles = root.GetProperty("gambles").EnumerateArray().Select(row => new ActivityGambleRule(
            row.GetProperty("id").GetString()!, row.GetProperty("attemptsGlobal").GetString(),
            row.GetProperty("successesGlobal").GetString(), row.GetProperty("failuresGlobal").GetString(),
            row.GetProperty("branchGlobals").EnumerateArray().Select(item => item.GetString()!).ToImmutableArray(),
            row.GetProperty("outputRawcodes").EnumerateArray().Select(item => item.GetString()!).ToImmutableHashSet(StringComparer.Ordinal),
            row.GetProperty("sourceFunctions").Clone())).ToImmutableArray();
    }

    internal static Map2321ActivityRules LoadBundled() => LoadBundled("2.321");

    internal static Map2321ActivityRules LoadBundled(string mapVersion)
    {
        if (mapVersion is not ("2.321" or "2.322" or "2.323")) throw new InvalidDataException("Unsupported activity source.");
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data",
            mapVersion == "2.323" ? "map-activity-2323.json" : mapVersion == "2.322" ? "map-activity-2322.json" : FileName));
        if (stream.Length is <= 0 or > 1024 * 1024) throw new InvalidDataException("Activity rule size rejected.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return Load(bytes, mapVersion);
    }

    internal static Map2321ActivityRules Load(byte[] bytes, string mapVersion = "2.321")
    {
        if (mapVersion is not ("2.321" or "2.322" or "2.323") || bytes is null || bytes.Length is <= 0 or > 1024 * 1024 ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(mapVersion == "2.323" ? Map2323SourceContract.ActivityDataSha256 : mapVersion == "2.322" ? "6a58adb41d2517b2412582cbd82375505ffc3f38e8b297d513713519896b1106" : DataSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Activity rule data pin mismatch.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("mapVersion").GetString() != mapVersion ||
            root.GetProperty("jassSha256").GetString() != (mapVersion == "2.323" ? Map2323SourceContract.JassSha256 : mapVersion == "2.322" ? Map2322SourceContract.JassSha256 : SourceSha256))
            throw new InvalidDataException("Activity rule source mismatch.");
        return new(root);
    }
}
