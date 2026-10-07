using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2320BundleIntegrationTests
{
    // Independent of ExpectedMembers, and copied exclusively from published output Data.
    private static readonly string[] Files =
    [
        "map-source-manifest-2320.json", "map-source-metadata-2320.json",
        "story-progression-2320.json", "navigation-mechanics-2320.json",
        "map-recipes-2320.json", "map-mechanics-2320.json",
        "map-combine-commands-2320.txt", "utility-board-2320.json"
    ];

    [Fact]
    public void BundledDataLoadsCompleteOfflineContract()
    {
        Complete(Map2320DataBundle.LoadBundled());
        Assert.Equal(Files.Skip(1), Map2320DataBundle.ExpectedMembers.Select(x => x.File));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EightPublishedFilesAloneLoadWithoutSourceOrAnalysisFiles(string newline)
    {
        using var sandbox = new Sandbox();
        foreach (var file in Files)
        {
            var path = Path.Combine(sandbox.Data, file);
            var text = File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");
            File.WriteAllText(path, text.Replace("\n", newline), new UTF8Encoding(false));
        }
        Assert.Equal(Files.OrderBy(x => x), Directory.GetFiles(sandbox.Data).Select(Path.GetFileName).OrderBy(x => x));
        Assert.Empty(Directory.GetDirectories(sandbox.Data));
        Assert.Single(Directory.GetDirectories(sandbox.Root));
        Assert.Empty(Directory.GetFiles(sandbox.Root));
        var isolated = Map2320DataBundle.LoadFromDirectory(sandbox.Data);
        Complete(isolated);
        Assert.Equal(Map2320DataBundle.LoadBundled().Fingerprint, isolated.Fingerprint);
    }

    public static IEnumerable<object[]> RequiredFiles() => Files.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(RequiredFiles))]
    public void EveryRequiredFileIsRequiredWithoutFallback(string file)
    {
        using var sandbox = new Sandbox();
        File.Delete(Path.Combine(sandbox.Data, file));
        Assert.ThrowsAny<IOException>(() => Map2320DataBundle.LoadFromDirectory(sandbox.Data));
    }

    [Theory]
    [MemberData(nameof(RequiredFiles))]
    public void EveryFileMutationRejectsWithoutFallback(string file)
    {
        using var sandbox = new Sandbox();
        File.AppendAllText(Path.Combine(sandbox.Data, file), "X", new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() => Map2320DataBundle.LoadFromDirectory(sandbox.Data));
    }

    [Theory]
    [InlineData("extra-member")]
    [InlineData("missing-member")]
    [InlineData("reordered-members")]
    [InlineData("duplicate-member")]
    [InlineData("extra-root-field")]
    [InlineData("extra-member-field")]
    [InlineData("missing-root-field")]
    [InlineData("missing-member-field")]
    [InlineData("duplicate-root-field")]
    [InlineData("duplicate-member-field")]
    [InlineData("wrong-identifier")]
    [InlineData("wrong-size")]
    [InlineData("wrong-hash")]
    [InlineData("wrong-archive")]
    [InlineData("wrong-map")]
    [InlineData("wrong-schema")]
    [InlineData("case-confusion")]
    [InlineData("parent-path")]
    [InlineData("relative-path")]
    [InlineData("absolute-path")]
    [InlineData("separator-confusion")]
    [InlineData("member-path-swap")]
    public void ManifestMustMatchExactClosedContract(string mutation)
    {
        using var sandbox = new Sandbox();
        var path = Path.Combine(sandbox.Data, Files[0]);
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var members = manifest["Members"]!.AsArray();
        var first = members[0]!.AsObject();
        switch (mutation)
        {
            case "extra-member": members.Add(new JsonObject { ["Identifier"] = "extra", ["File"] = "extra.json", ["NormalizedBytes"] = 1, ["Sha256"] = new string('0', 64) }); break;
            case "missing-member": members.RemoveAt(6); break;
            case "reordered-members": var item = first.DeepClone(); members.RemoveAt(0); members.Add(item); break;
            case "duplicate-member": members[6] = first.DeepClone(); break;
            case "extra-root-field": manifest["LiveRecognitionSupported"] = true; break;
            case "extra-member-field": first["Optional"] = true; break;
            case "missing-root-field": manifest.Remove("ArchiveSha256"); break;
            case "missing-member-field": first.Remove("Sha256"); break;
            case "wrong-identifier": first["Identifier"] = "story"; break;
            case "wrong-size": first["NormalizedBytes"] = 1; break;
            case "wrong-hash": first["Sha256"] = new string('0', 64); break;
            case "wrong-archive": manifest["ArchiveSha256"] = new string('0', 64); break;
            case "wrong-map": manifest["MapVersion"] = "2.314"; break;
            case "wrong-schema": manifest["SchemaVersion"] = 2; break;
            case "case-confusion": first["File"] = Files[1].ToUpperInvariant(); break;
            case "parent-path": first["File"] = "../Data/" + Files[1]; break;
            case "relative-path": first["File"] = "./" + Files[1]; break;
            case "absolute-path": first["File"] = Path.Combine(sandbox.Data, Files[1]); break;
            case "separator-confusion": first["File"] = "..\\Data\\" + Files[1]; break;
            case "member-path-swap": first["File"] = Files[2]; break;
        }
        var json = manifest.ToJsonString();
        if (mutation == "duplicate-root-field") json = "{\"MapVersion\":\"2.320\"," + json[1..];
        if (mutation == "duplicate-member-field") json = json.Replace("\"Identifier\":\"source\"", "\"Identifier\":\"source\",\"Identifier\":\"source\"");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        Assert.Throws<InvalidDataException>(() => Map2320DataBundle.LoadFromDirectory(sandbox.Data));
    }

    [Theory]
    [InlineData("2.319")]
    [InlineData("2.324")]
    [InlineData("2.320 ")]
    [InlineData("")]
    public void CatalogRejectsUnknownExplicitMapVersion(string version)
    {
        var catalog = new DataCatalog();
        Assert.Throws<InvalidDataException>(() => catalog.Load(mapVersion: version));
        Assert.Null(catalog.OfflineBundle);
        Assert.Equal("2.314", catalog.MapVersion);
    }

    [Fact]
    public void EffectiveAkainuAndA12CUseActualSourceNotHistoricalOrTestRecipes()
    {
        var catalog = Current();
        Complete(catalog.OfflineBundle!);
        Assert.Equal("2.320", catalog.MapVersion);
        var akainu = Ingredients(catalog, "590H");
        Assert.Equal(new[] { "J10h", "MC0h", "S40h", "Z30h" }, UnitKeys(akainu));
        Assert.DoesNotContain("R00h", akainu.Keys);
        var recipe = Ingredients(catalog, "CB0h");
        Assert.Equal(4, UnitKeys(recipe).Count());
        Assert.Equal(7, recipe["LUMBER"]);
        Assert.Equal(10000, recipe["GOLD"]);
        Assert.Equal("A12C", catalog.OfflineBundle!.Recipes.Project("CB0h")!.RecipeId);
        Assert.DoesNotContain(catalog.OfflineBundle.Recipes.ProjectAll().Values, x => x.RecipeId == "TEST");
    }

    [Fact]
    public void AllEffectiveSourceRecipesOverrideTmoAfterProjection()
    {
        var catalog = Current();
        var checkedCount = 0;
        foreach (var projection in catalog.OfflineBundle!.Recipes.ProjectAll().Values)
        {
            if (MapRecipeMechanics.IsNikaCode(projection.AppRawcode)) continue;
            if (!catalog.RawcodeCatalog.ContainsKey(projection.AppRawcode)) continue;
            Assert.Equal(projection.IngredientsByAppRawcode.OrderBy(x => x.Key, StringComparer.Ordinal),
                Ingredients(catalog, projection.AppRawcode).OrderBy(x => x.Key, StringComparer.Ordinal));
            checkedCount++;
        }
        Assert.True(checkedCount > 200, $"Only {checkedCount} source projections reached the catalog.");
        Assert.Equal(1, Ingredients(catalog, "AA0H")[RecipeWildcards.AnySeraphim]);
    }

    [Fact]
    public void SourceConditionsStayUnresolvedIncludingKingUbanAndGold()
    {
        var catalog = Current();
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var projection in catalog.OfflineBundle!.Recipes.ProjectAll().Values)
        {
            if (MapRecipeMechanics.IsNikaCode(projection.AppRawcode) || !catalog.RawcodeCatalog.ContainsKey(projection.AppRawcode)) continue;
            var expected = projection.ConditionalRequirements.Select(x => x.Source.Kind).ToList();
            if (projection.IngredientsByAppRawcode.GetValueOrDefault("GOLD") > 0) expected.Add("GOLD");
            if (expected.Count == 0) continue;
            var unit = catalog.Unit("rawcode:" + projection.AppRawcode);
            Assert.NotNull(unit.RecipeConditions);
            foreach (var kind in expected)
            {
                kinds.Add(kind);
                Assert.Contains(kind, unit.RecipeConditions.UnresolvedSourceConditions);
            }
            var result = RecipeConditionEvaluator.Evaluate(unit, Context(1000000));
            Assert.Equal(RecipeConditionStatus.Unknown, result.Status);
            Assert.Contains(unit.RecipeConditions.UnresolvedSourceConditions!, result.Reason);
        }
        Assert.Contains("KING", kinds);
        Assert.Contains("UBAN", kinds);
        Assert.Contains("GOLD", kinds);
    }

    [Fact]
    public void SimpleWoodCostRequiresKnownSufficientCurrentObservation()
    {
        var catalog = Current();
        var candidates = catalog.OfflineBundle!.Recipes.ProjectAll().Values.Where(p =>
            !MapRecipeMechanics.IsNikaCode(p.AppRawcode) && catalog.RawcodeCatalog.ContainsKey(p.AppRawcode) &&
            p.ConditionalRequirements.Length == 0 && p.IngredientsByAppRawcode.GetValueOrDefault("GOLD") == 0 &&
            p.IngredientsByAppRawcode.GetValueOrDefault("LUMBER") > 0).ToArray();
        Assert.NotEmpty(candidates);
        foreach (var projection in candidates)
        {
            var unit = catalog.Unit("rawcode:" + projection.AppRawcode);
            var wood = projection.IngredientsByAppRawcode["LUMBER"];
            Assert.NotNull(unit.RecipeConditions);
            Assert.Equal(wood, unit.RecipeConditions.Lumber);
            Assert.Null(unit.RecipeConditions.UnresolvedSourceConditions);
            Assert.Equal(RecipeConditionStatus.Unknown, RecipeConditionEvaluator.Evaluate(unit).Status);
            Assert.Equal(RecipeConditionStatus.Unknown, RecipeConditionEvaluator.Evaluate(unit, Context(null)).Status);
            Assert.Equal(RecipeConditionStatus.Blocked, RecipeConditionEvaluator.Evaluate(unit, Context(wood - 1)).Status);
            Assert.True(RecipeConditionEvaluator.Evaluate(unit, Context(wood)).IsSatisfied);
        }
    }

    [Theory]
    [InlineData("KB0H", "990H")]
    [InlineData("KB0H", "2B0H")]
    [InlineData("KB0H_", "990H")]
    [InlineData("KB0H_", "2B0H")]
    public void NikaAliasesPreserveConditionsAndConsumeOneRealAlternative(string code, string hero)
    {
        var catalog = Current();
        var nika = catalog.Unit("rawcode:" + code);
        Assert.Equal(catalog.OfflineBundle!.Nika.Conditions, nika.RecipeConditions);
        Assert.Equal(new[] { "태양의신", "nika et" }, nika.CombineCommands);
        Assert.Equal(1, nika.Recipe[RecipeWildcards.AnyNika]);
        Assert.DoesNotContain("rawcode:700I", nika.Recipe.Keys);
        Assert.False(RecipeConditionEvaluator.Evaluate(nika).IsSatisfied);
        Assert.True(RecipeConditionEvaluator.Evaluate(nika, Context(5)).IsSatisfied);
        var inventory = nika.Recipe.Where(x => x.Key != RecipeWildcards.AnyNika).ToDictionary(x => x.Key, x => x.Value);
        inventory["rawcode:" + hero] = 1;
        var consumed = RecipeWildcards.AllocateDirect(nika, inventory, catalog.Unit);
        Assert.NotNull(consumed);
        Assert.Equal(1L, consumed["rawcode:" + hero]);
        inventory.Remove("rawcode:" + hero);
        inventory[RecipeWildcards.AnyNika] = 99;
        inventory["rawcode:700I"] = 99;
        Assert.Null(RecipeWildcards.AllocateDirect(nika, inventory, catalog.Unit));
    }

    [Fact]
    public void DefaultAndExplicitLegacyKeepTmoPrecedenceWithoutOfflineBundleLeak()
    {
        var defaults = new DataCatalog(); defaults.Load();
        var explicitLegacy = new DataCatalog(); explicitLegacy.Load(mapVersion: "2.314");
        var reloaded = Current(); reloaded.Load();
        foreach (var catalog in new[] { defaults, explicitLegacy, reloaded })
        {
            Assert.Equal("2.314", catalog.MapVersion);
            Assert.Null(catalog.OfflineBundle);
            Assert.Equal(new[] { "J10h", "MC0h", "R00h", "S40h", "Z30h" }, UnitKeys(Ingredients(catalog, "590H")));
            Assert.Null(catalog.Unit("rawcode:KB0H").RecipeConditions);
            Assert.Equal(new[] { "030h", "G60h", "HA0h", "W60h" }, UnitKeys(Ingredients(catalog, "CB0h")));
        }
    }

    private static void Complete(Map2320DataBundle bundle)
    {
        Assert.NotNull(bundle.Source);
        Assert.Equal(265, bundle.Recipes.Document.Recipes.Length);
        Assert.Equal(14, bundle.Story.Stages.Length);
        Assert.Equal(15, bundle.Navigation.Options.Length);
        Assert.Equal(221, bundle.UtilityBoard.Rows.Length);
        Assert.False(bundle.LiveRecognitionSupported);
        Assert.False(bundle.AutomaticNavigationScoringSupported);
        Assert.All(bundle.Recipes.ProjectAll().Values, x => Assert.True(x.NoAutomaticEligibilityClaim));
        Assert.False(string.IsNullOrWhiteSpace(bundle.Commands));
        Assert.Equal(64, bundle.Fingerprint.Length);
    }

    private static DataCatalog Current()
    {
        var catalog = new DataCatalog(); catalog.Load(mapVersion: "2.320"); return catalog;
    }
    private static IEnumerable<string> UnitKeys(Dictionary<string, int> ingredients) =>
        ingredients.Keys.Where(x => x is not "LUMBER" and not "GOLD").OrderBy(x => x, StringComparer.Ordinal);
    private static Dictionary<string, int> Ingredients(DataCatalog catalog, string rawcode) =>
        catalog.Unit("rawcode:" + rawcode).Recipe.GroupBy(x => x.Key == RecipeWildcards.AnySeraphim
            ? x.Key : catalog.Unit(x.Key).Rawcodes.FirstOrDefault() ?? x.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value), StringComparer.Ordinal);
    private static RecipeConditionContext Context(int? wood)
    {
        var now = DateTimeOffset.UtcNow;
        return new("2.320", "bundle-offline-test", 0, now,
            new("2.320", "bundle-offline-test", 0, now, 1, new Dictionary<string, bool> { ["AI01"] = true }, wood));
    }
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "orand-bundle2320-" + Guid.NewGuid().ToString("N"));
        public string Data => Path.Combine(Root, "Data");
        public Sandbox()
        {
            Directory.CreateDirectory(Data);
            foreach (var file in Files)
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", file), Path.Combine(Data, file));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
