using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

// Closed source DTOs, deliberately separate from TMO identities and native eligibility.
public sealed record Map2320RecipeTerm(string Kind, string Id, int Count);
public sealed record Map2320RecipeRow(string RecipeId, int LineStart, int LineEnd, string RawSource,
    ImmutableArray<Map2320RecipeTerm> Outputs, ImmutableArray<Map2320RecipeTerm> Conditions);
public sealed record Map2320RecipeChoice(string OutputId, string RecipeId, int Line);
public sealed record Map2320RecipeHandler(string Kind, int Phase, string CodeAlias, string FunctionName,
    int RegistrationLine, int FunctionStart, int FunctionEnd, string RawSource);
public sealed record Map2320RecipeEvidence(string Name, int LineStart, int LineEnd, string RawSource);
public sealed record Map2320RecipeDocument(int SchemaVersion, string MapVersion, string SourcePath, string SourceSha256,
    string OldSourcePath, string OldSourceSha256, ImmutableArray<Map2320RecipeRow> Recipes,
    ImmutableArray<Map2320RecipeChoice> ActiveChoices, ImmutableArray<Map2320RecipeHandler> Handlers,
    ImmutableArray<Map2320RecipeEvidence> Evidence, ImmutableArray<Map2320RecipeRow> OldRecipes,
    ImmutableArray<string> ChangedIds, ImmutableArray<string> RemovedIds);
public sealed record Map2320ConditionalRequirement(Map2320RecipeTerm Source, string Interpretation,
    bool HasValidationHandler, bool HasConsumptionHandler, int SourceLine);
public sealed record Map2320RecipeProjection(string AppRawcode, string RecipeId, string SelectionEvidence,
    ImmutableDictionary<string, int> IngredientsByAppRawcode,
    ImmutableArray<Map2320ConditionalRequirement> ConditionalRequirements,
    ImmutableArray<Map2320RecipeTerm> UiMetadata)
{
    public bool NoAutomaticEligibilityClaim => true;
    public bool HasUnresolvedRequirements => ConditionalRequirements.Length != 0;
}

/// <summary>Bounded exact 2.320 planning data, not full native/archive approval. No automatic craft eligibility.</summary>
public sealed class Map2320RecipeRegistry
{
    public const string DataFileName = "map-recipes-2320.json";
    public const int MaxDataBytes = 2 * 1024 * 1024;
    public const string DataSha256 = "a4f12b6300b697e9fea24c0e41f8eb7200bf2b3ab7b9b4046d9169717b2bbab5";
    public const string SemanticSha256 = "3197634d8d9ae3bb1165e825dd07f6449a5b356527789e149868d16b23407156";
    public const string SourceSha256 = "6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 24,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    public Map2320RecipeDocument Document { get; }
    private Map2320RecipeRegistry(Map2320RecipeDocument document) => Document = document;
    public static Map2320RecipeRegistry LoadBundled()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", DataFileName));
        if (stream.Length <= 0 || stream.Length > MaxDataBytes) throw new InvalidDataException("Recipe data size bound.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (Hash(bytes) != DataSha256) throw new InvalidDataException("Bundled recipe byte pin mismatch.");
        return Load(bytes);
    }
    public static Map2320RecipeRegistry Load(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxDataBytes) throw new InvalidDataException("Recipe data size bound.");
        try
        {
            using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            // Exact semantic pin closes required fields, ranges, counts, IDs, every source line and old/new fixtures.
            // Canonicalization permits harmless whitespace/property order, rejects duplicate keys and fractional numbers.
            if (Hash(Encoding.UTF8.GetBytes(Canonical(json.RootElement))) != SemanticSha256)
                throw new InvalidDataException("Recipe semantic/source contract mismatch.");
            var doc = JsonSerializer.Deserialize<Map2320RecipeDocument>(bytes, Options)
                ?? throw new InvalidDataException("Null recipe document.");
            if (doc.SchemaVersion != 1 || doc.MapVersion != "2.320" || doc.SourceSha256 != SourceSha256 ||
                doc.Recipes.Length != 265 || doc.OldRecipes.Length != 266 ||
                doc.Recipes.Select(r => r.RecipeId).Distinct(StringComparer.Ordinal).Count() != 265 ||
                doc.Recipes.Any(r => r.Outputs.Length != 1 || r.Outputs[0].Kind != "UNIT" || r.Outputs[0].Count != 1 ||
                    r.LineStart < 58505 || r.LineEnd > 68699 || r.Conditions.Any(t => t.Count <= 0)) ||
                doc.ActiveChoices.Any(c => c.RecipeId == "TEST"))
                throw new InvalidDataException("Recipe structure mismatch.");
            return new(doc);
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid recipe JSON.", ex); }
    }
    public byte[] Export() => JsonSerializer.SerializeToUtf8Bytes(Document, Options);
    public void ValidateSource(ReadOnlySpan<byte> source, ReadOnlySpan<byte> oldSource)
    {
        if (Hash(source) != Document.SourceSha256 || Hash(oldSource) != Document.OldSourceSha256)
            throw new InvalidDataException("Extracted JASS source hash mismatch.");
    }
    public static string ToAppRawcode(string sourceRawcode)
    {
        if (sourceRawcode is null || sourceRawcode.Length != 4 || sourceRawcode.Any(c => c < 32 || c > 126))
            throw new ArgumentException("A four-character source rawcode is required.", nameof(sourceRawcode));
        return new string(sourceRawcode.Reverse().ToArray());
    }
    /// <summary>Input and ordinary ingredient keys are APP byte order. Unknown outputs remain base units (null).
    /// h0AN alone has a verified seraphim alias. H08T is exposed, never flattened into a fake unit.
    /// ITEM/SKPT/CHCT/IFCT/PICK/RMAX/UBAN/KING require separate observations, never TMO token inference.</summary>
    public Map2320RecipeProjection? Project(string appRawcode)
    {
        if (appRawcode is null || appRawcode.Length != 4) return null;
        var sourceId = ToAppRawcode(appRawcode);
        var candidates = Document.Recipes.Where(r => r.RecipeId != "TEST" && r.Outputs[0].Id == sourceId).ToArray();
        var choice = Document.ActiveChoices.LastOrDefault(c => c.OutputId == sourceId);
        var recipe = choice is not null ? candidates.SingleOrDefault(r => r.RecipeId == choice.RecipeId) :
            candidates.Length == 1 ? candidates[0] : null;
        if (recipe is null) return null;
        var materials = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var conditions = ImmutableArray.CreateBuilder<Map2320ConditionalRequirement>();
        var metadata = ImmutableArray.CreateBuilder<Map2320RecipeTerm>();
        for (var i = 0; i < recipe.Conditions.Length; i++)
        {
            var term = recipe.Conditions[i];
            if (term.Kind is "UPUN" or "SPEC" or "GREN" or "RDUN") { metadata.Add(term); continue; }
            string? key = term.Kind switch
            {
                "UNIT" when term.Id == "H08T" => null,
                "UNIT" when term.Id == "h0AN" => "seraphim_any",
                "UNIT" => ToAppRawcode(term.Id),
                "WOOD" => "LUMBER",
                "GOLD" => "GOLD",
                _ => null
            };
            if (key is not null) materials[key] = checked(materials.GetValueOrDefault(key) + term.Count);
            else
            {
                var interpretation = term.Kind switch
                {
                    "UNIT" => "NikaAlternative: H08T source alias; resolve with Nika integration, not a concrete unit",
                    "ITEM" => "Boolean player token; no TMO token-to-AI identity inferred",
                    "PICK" => "Spell-target ability requirement and target consumption",
                    "IFCT" => "Dynamic lumber cost: Hb + qy[player], then qy increases by Ky[player]",
                    "SKPT" => "Player FOOD_USED resource requirement and consumption",
                    "CHCT" => "Player Hy change counter requirement and consumption",
                    "RMAX" => "Maximum round constraint; registered consumption callback is empty",
                    "UBAN" => "Player unit-ban observation required",
                    "KING" => "Player vg/oy navigation restriction observation required",
                    _ => "Unknown condition; separate source observation required"
                };
                conditions.Add(new(term, interpretation,
                    Document.Handlers.Any(h => h.Kind == term.Kind && h.Phase == 1),
                    Document.Handlers.Any(h => h.Kind == term.Kind && h.Phase == 2), recipe.LineStart + 9 + 5 * i));
            }
        }
        return new(appRawcode, recipe.RecipeId, choice is null ? "Unique registry output: planning only" : $"Lc active choice at source line {choice.Line}",
            materials.ToImmutable(), conditions.ToImmutable(), metadata.ToImmutable());
    }
    /// <summary>Non-mutating integration helper. Missing/out-of-registry entries retain their exact base recipe.
    /// The caller must retain ConditionalRequirements and NoAutomaticEligibilityClaim with every applied projection.
    /// This API intentionally does not edit UnitDefinition, TMO data, legacy recipes, or automatic eligibility.</summary>
    public IReadOnlyDictionary<string, Map2320RecipeProjection> ProjectAll() => Document.Recipes
        .Where(r => r.RecipeId != "TEST").Select(r => ToAppRawcode(r.Outputs[0].Id)).Distinct(StringComparer.Ordinal)
        .Select(Project).Where(p => p is not null).ToDictionary(p => p!.AppRawcode, p => p!, StringComparer.Ordinal);
    public Map2320RecipeProjection? Apply(string appRawcode, Action<Map2320RecipeProjection> applyPlanningProjection)
    {
        ArgumentNullException.ThrowIfNull(applyPlanningProjection);
        var projection = Project(appRawcode);
        if (projection is not null) applyPlanningProjection(projection);
        return projection;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Canonical(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = element.EnumerateObject().ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in properties) if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate recipe property.");
                return "{" + string.Join(",", properties.OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => JsonSerializer.Serialize(p.Name, Options) + ":" + Canonical(p.Value))) + "}";
            case JsonValueKind.Array: return "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]";
            case JsonValueKind.String: return JsonSerializer.Serialize(element.GetString(), Options);
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var n)) throw new InvalidDataException("Bounded integer required.");
                return n.ToString(CultureInfo.InvariantCulture);
            default: throw new InvalidDataException("Null/boolean/undefined recipe value forbidden.");
        }
    }
}
