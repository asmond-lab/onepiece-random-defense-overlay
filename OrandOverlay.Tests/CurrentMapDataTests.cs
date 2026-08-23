using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CurrentMapDataTests
{
    [Fact]
    public void GarpRecipeUsesDragonLegendInsteadOfBlackMaria()
    {
        var catalog = LoadCatalog();
        var ingredientRawcodes = catalog.Unit("rawcode:C40h").Recipe.Keys
            .SelectMany(unitId => catalog.Unit(unitId).Rawcodes)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("W20h", ingredientRawcodes);
        Assert.Contains("E30h", ingredientRawcodes);
        Assert.Contains("V20h", ingredientRawcodes);
        Assert.DoesNotContain("X20h", ingredientRawcodes);
    }

    [Fact]
    public void EveryCurrentMapRecipeOverrideIsApplied()
    {
        var catalog = LoadCatalog();
        var path = Path.Combine(AppContext.BaseDirectory, "Data",
            "map-recipe-overrides-2314.txt");
        var entries = File.ReadLines(path)
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(ParseOverride)
            .ToList();
        Assert.Equal(64, entries.Count);

        var mismatches = new List<string>();
        foreach (var (rawcode, sourceRecipe) in entries)
        {
            var expected = sourceRecipe.ToDictionary(
                item => ResolveIngredientUnitId(catalog, item.Rawcode),
                item => item.Count, StringComparer.OrdinalIgnoreCase);
            var actual = catalog.Unit("rawcode:" + rawcode).Recipe;
            if (!expected.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .SequenceEqual(actual.OrderBy(pair => pair.Key,
                        StringComparer.OrdinalIgnoreCase)))
                mismatches.Add(rawcode);
        }

        Assert.True(mismatches.Count == 0,
            "2.314 맵 조합식 오버라이드 미적용: " + string.Join(", ", mismatches));
    }

    [Fact]
    public void EveryCurrentNonItemUnitUsesItsBundledRawcodeImage()
    {
        var catalog = LoadCatalog();
        var intentionallyTextOnly = new HashSet<string>(
            ["FB0h", "I60h", "Z60h"], StringComparer.Ordinal);
        var missing = catalog.AllUnits
            .Where(unit => unit.Rawcodes.Count > 0)
            .Where(unit => BaseTier(unit.Tier) is not ("아이템" or "자원"))
            .Where(unit => unit.Rawcodes.All(rawcode => !intentionallyTextOnly.Contains(rawcode)))
            .Where(unit => !Path.IsPathRooted(unit.Image) || !File.Exists(unit.Image))
            .Select(unit => $"{unit.Id}:{unit.Name}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(missing.Count == 0,
            "로컬 rawcode 이미지가 연결되지 않은 유닛: " + string.Join(", ", missing));
        Assert.EndsWith("rawcode_U40h.png", catalog.Unit("rawcode:X20h").Image,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("rawcode_800h.png", catalog.Unit("rawcode:K30h").Image,
            StringComparison.OrdinalIgnoreCase);
        foreach (var rawcode in intentionallyTextOnly)
            Assert.True(string.IsNullOrWhiteSpace(catalog.Unit("rawcode:" + rawcode).Image),
                $"{rawcode}는 잘못된 구 이미지 대신 글자 타일을 사용해야 합니다.");
    }

    [Fact]
    public void MainCardRendererPreservesBlackMariaCurrentMapImageAlias()
    {
        var catalog = LoadCatalog();
        var blackMaria = catalog.Unit("rawcode:X20h");

        var resolved = UnitImageFactory.ResolveBundledImage(
            blackMaria.Image, blackMaria.Id, blackMaria.Rawcodes);

        Assert.EndsWith("rawcode_U40h.png", resolved,
            StringComparison.OrdinalIgnoreCase);
    }

    private static DataCatalog LoadCatalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }

    private static (string Rawcode, List<(string Rawcode, int Count)> Recipe) ParseOverride(
        string line)
    {
        var halves = line.Split('=', 2);
        var recipe = halves[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(item =>
            {
                var pair = item.Split(':', 2);
                return (pair[0], int.Parse(pair[1]));
            })
            .ToList();
        return (halves[0], recipe);
    }

    private static string ResolveIngredientUnitId(DataCatalog catalog, string rawcode)
    {
        var owner = catalog.AllUnits.FirstOrDefault(unit =>
            unit.Tags.All(tag => tag != "rawcode-catalog") &&
            unit.Rawcodes.Contains(rawcode, StringComparer.Ordinal));
        return owner?.Id ?? "rawcode:" + rawcode;
    }

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}
