using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>All active 2.322 output registrations; TEST is never a planning projection.</summary>
public sealed class Map2322RecipeRegistry
{
    private readonly ImmutableDictionary<string, Map2320RecipeProjection> projections;
    private Map2322RecipeRegistry(ImmutableDictionary<string, Map2320RecipeProjection> projections) => this.projections = projections;

    internal static Map2322RecipeRegistry Load(byte[] bytes, string mapVersion = "2.322")
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        Map2322DataBundle.CheckIdentity(root, mapVersion);
        if (root.GetProperty("sourceSha256").GetString() != (mapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.JassSha256 : Map2322SourceContract.JassSha256) ||
            root.GetProperty("coverage").GetString() != "All 265 active registered outputs; TEST excluded")
            throw new InvalidDataException("Unapproved 2.322 recipe source.");
        var rows = root.GetProperty("recipes");
        var choices = root.GetProperty("activeChoices");
        if (rows.GetArrayLength() != 266 || choices.GetArrayLength() != 265)
            throw new InvalidDataException("Incomplete 2.322 recipe registry.");
        var recipes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            var recipeId = row.GetProperty("recipeId").GetString()!;
            if (!recipes.TryAdd(recipeId, row)) throw new InvalidDataException("Duplicate recipe ID.");
        }
        if (!recipes.ContainsKey("TEST")) throw new InvalidDataException("Missing test registration.");
        var projected = ImmutableDictionary.CreateBuilder<string, Map2320RecipeProjection>(StringComparer.Ordinal);
        foreach (var choice in choices.EnumerateArray())
        {
            var sourceId = choice.GetProperty("outputId").GetString()!;
            var recipeId = choice.GetProperty("recipeId").GetString()!;
            if (recipeId == "TEST" || !recipes.TryGetValue(recipeId, out var row)) throw new InvalidDataException("Inactive or missing recipe.");
            var output = row.GetProperty("output");
            if (output.GetProperty("kind").GetString() != "UNIT" || output.GetProperty("count").GetInt32() != 1 ||
                output.GetProperty("id").GetString() != sourceId)
                throw new InvalidDataException("Recipe active output mismatch.");
            var materials = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
            var conditions = ImmutableArray.CreateBuilder<Map2320ConditionalRequirement>();
            var metadata = ImmutableArray.CreateBuilder<Map2320RecipeTerm>();
            var sourceLine = row.GetProperty("source").GetProperty("startLine").GetInt32();
            var index = 0;
            foreach (var entry in row.GetProperty("conditions").EnumerateArray())
            {
                var kind = entry.GetProperty("kind").GetString()!;
                var id = entry.GetProperty("id").GetString()!;
                var count = entry.GetProperty("count").GetInt32();
                if (count <= 0) throw new InvalidDataException("Invalid recipe quantity.");
                var term = new Map2320RecipeTerm(kind, id, count);
                if (kind is "UPUN" or "SPEC" or "GREN" or "RDUN") metadata.Add(term);
                else
                {
                    var key = kind switch
                    {
                        "UNIT" when id == "H08T" => null,
                        "UNIT" when id == "h0AN" => "seraphim_any",
                        "UNIT" => Map2320RecipeRegistry.ToAppRawcode(id),
                        "GOLD" => "GOLD",
                        "WOOD" => "LUMBER",
                        _ => null
                    };
                    if (key is not null) materials[key] = checked(materials.GetValueOrDefault(key) + count);
                    else conditions.Add(new(term, kind switch
                    {
                        "PICK" => "Observed spell-target ability requirement and target consumption",
                        "KING" => "Unresolved player navigation restriction; not an owned-unit ingredient",
                        "UNIT" => "NikaAlternative: H08T source alias; not a concrete unit",
                        "ITEM" => "Player boolean token; no catalog item inferred",
                        "IFCT" => "Dynamic lumber requirement",
                        "SKPT" => "Player food resource condition",
                        "CHCT" => "Player change counter condition",
                        "RMAX" => "Maximum round condition",
                        "UBAN" => "Player unit-ban condition",
                        _ => "Unresolved source requirement"
                    }, kind == "PICK", kind == "PICK", sourceLine + 9 + 5 * index));
                }
                index++;
            }
            var appId = Map2320RecipeRegistry.ToAppRawcode(sourceId);
            if (!projected.TryAdd(appId, new(appId, recipeId, $"Active {mapVersion} output registration at line {choice.GetProperty("line").GetInt32()}",
                materials.ToImmutable(), conditions.ToImmutable(), metadata.ToImmutable())))
                throw new InvalidDataException("Duplicate active output.");
        }
        if (projected.Count != 265) throw new InvalidDataException("Incomplete active projections.");
        return new(projected.ToImmutable());
    }

    public Map2320RecipeProjection? Project(string appRawcode) => appRawcode is not null && projections.TryGetValue(appRawcode, out var value) ? value : null;
    public IReadOnlyDictionary<string, Map2320RecipeProjection> ProjectAll() => projections;
}
