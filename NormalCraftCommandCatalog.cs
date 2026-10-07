using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

internal sealed class NormalCraftCommandCatalog
{
    internal const string DataFileName = "map-combine-hotkeys-2320.json";
    internal const string DataSha256 = "63082048eecd9b4f3d71ad79ee0d6741db25cb7f482a423a82926dd2aae139cb";
    private const string AbilitySha256 = "d3fde16020faf5f3e0becdbf571927d3acd77be9d5610f3e07a2b2584ac60aa5";
    private const string UnitSha256 = "18b5dba815b7baed4e56683136883e9b239fc4e8ac63491bc57696fb05586b2a";
    private const string StringsSha256 = "4b9c531cde67c979eae0ba289d133862f57714cd7dc0cf3bbac3376b52ee6946";
    internal static NormalCraftCommandCatalog Empty { get; } = new([]);
    private readonly ImmutableDictionary<string, NormalCraftCommand> _entries;
    internal IReadOnlyCollection<NormalCraftCommand> Entries => _entries.Values.ToArray();

    private NormalCraftCommandCatalog(IEnumerable<NormalCraftCommand> entries) =>
        _entries = entries.ToImmutableDictionary(entry => entry.Result, StringComparer.Ordinal);

    internal NormalCraftCommand? Find(UnitDefinition unit) => unit.Rawcodes
        .Select(code => _entries.GetValueOrDefault(code)).FirstOrDefault(entry => entry is not null);

    internal static NormalCraftCommandCatalog Load2322(Map2322DataBundle bundle) => new(
        bundle.Hotkeys.Where(key => key.Result != "2C0h" &&
            bundle.Recipes.Project(key.Result) is { HasUnresolvedRequirements: false } projection &&
            key.HostRawcodes.Any(host => projection.IngredientsByAppRawcode.ContainsKey(host)))
        .Select(key => new NormalCraftCommand(key.Result, key.Ability, key.Key,
            key.HostRawcodes.First(host => bundle.Recipes.Project(key.Result)!.IngredientsByAppRawcode.ContainsKey(host)),
            1, 0, 0, 0, null)));

    internal static NormalCraftCommandCatalog LoadBundled(Map2320RecipeRegistry registry)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", DataFileName);
            var file = new FileInfo(path);
            if (file.LinkTarget is not null || file.Length > 100_000) return Empty;
            return Load(File.ReadAllBytes(path), registry);
        }
        catch (IOException) { return Empty; }
        catch (UnauthorizedAccessException) { return Empty; }
        catch (InvalidDataException) { return Empty; }
    }

    internal static NormalCraftCommandCatalog Load(ReadOnlySpan<byte> bytes, Map2320RecipeRegistry registry)
    {
        if (bytes.Length is 0 or > 100_000) throw new InvalidDataException("Craft command size bound.");
        byte[] normalized;
        try { normalized = Map2320DataBundle.Normalize(bytes.ToArray()); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Craft command encoding.", exception); }
        if (!Convert.ToHexString(SHA256.HashData(normalized)).Equals(DataSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Craft command source pin mismatch.");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 8
        };
        var document = JsonSerializer.Deserialize<Document>(normalized, options)
            ?? throw new InvalidDataException("Craft command document missing.");
        if (document.SchemaVersion != 1 || document.MapVersion != "2.320" || document.SourceSha256 != Map2320RecipeRegistry.SourceSha256 ||
            document.AbilitySha256 != AbilitySha256 || document.UnitSha256 != UnitSha256 || document.StringsSha256 != StringsSha256 ||
            document.Entries.IsDefault || document.Entries.Length != 155)
            throw new InvalidDataException("Craft command source contract.");
        foreach (var entry in document.Entries)
        {
            var projection = registry.Project(entry.Result);
            var recipe = registry.Document.Recipes.SingleOrDefault(row => row.RecipeId == entry.Ability);
            if (projection?.RecipeId != entry.Ability || recipe is null || recipe.LineStart != entry.RecipeLine ||
                entry.Key.Length != 1 || entry.Key[0] is < 'A' or > 'Z' || entry.OutputCount != 1 ||
                recipe.Outputs.Length != 1 || recipe.Outputs[0].Count != entry.OutputCount ||
                !projection.IngredientsByAppRawcode.ContainsKey(entry.SelectionRawcode))
                throw new InvalidDataException("Craft command recipe relation.");
        }
        return new(document.Entries);
    }

    private sealed record Document(int SchemaVersion, string MapVersion, string SourceSha256,
        string AbilitySha256, string UnitSha256, string StringsSha256, ImmutableArray<NormalCraftCommand> Entries);
}

internal sealed record NormalCraftCommand(string Result, string Ability, string Key,
    string SelectionRawcode, int OutputCount, int RecipeLine, int HotkeyOffset,
    int HostAbilityOffset, int? HotkeyStringId);
