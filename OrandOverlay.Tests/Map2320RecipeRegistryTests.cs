using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2320RecipeRegistryTests
{
    private static Map2320RecipeRegistry Registry() => Map2320RecipeRegistry.LoadBundled();
    [Fact] public void All265RowsRoundTripWithCompleteSourceAndOnlyOneChangeOneRemoval()
    {
        var registry = Registry();
        var doc = Map2320RecipeRegistry.Load(registry.Export()).Document;
        Assert.Equal(265, doc.Recipes.Length);
        Assert.Equal(266, doc.OldRecipes.Length);
        Assert.Equal(264, doc.Recipes.Select(r => r.Outputs.Single().Id).Distinct().Count());
        foreach (var row in doc.Recipes.Concat(doc.OldRecipes))
        {
            Assert.Equal(row.LineEnd - row.LineStart + 1, row.RawSource.Split('\n').Length);
            Assert.Equal(9 + 5 * row.Conditions.Length, row.RawSource.Split('\n').Length);
            Assert.All(row.Outputs, t => { Assert.Equal("UNIT", t.Kind); Assert.Equal(1, t.Count); });
            Assert.Equal(1 + row.Conditions.Length, Regex.Matches(row.RawSource, @"call SaveInteger\((Zb|Rb|hG|pG),").Count);
        }
        static string Terms(Map2320RecipeRow r) => JsonSerializer.Serialize(new { r.Outputs, r.Conditions });
        var old = doc.OldRecipes.ToDictionary(r => r.RecipeId);
        Assert.Equal(new[] { "ET02" }, doc.Recipes.Where(r => Terms(r) != Terms(old[r.RecipeId])).Select(r => r.RecipeId));
        Assert.Equal(new[] { "ET03" }, old.Keys.Except(doc.Recipes.Select(r => r.RecipeId)));
        Assert.Equal(new[] { "ET02" }, doc.ChangedIds);
        Assert.Equal(new[] { "ET03" }, doc.RemovedIds);
    }
    [Fact] public void ActualA12CNotTestAndAkainuHasFourUnitsWithoutInventedR00h()
    {
        var r = Registry();
        var p = r.Project("CB0h")!;
        Assert.Equal("A12C", p.RecipeId);
        Assert.Equal(7, p.IngredientsByAppRawcode["LUMBER"]);
        Assert.Equal(10000, p.IngredientsByAppRawcode["GOLD"]);
        Assert.Equal(4, p.IngredientsByAppRawcode.Count(k => k.Key is not "LUMBER" and not "GOLD"));
        Assert.DoesNotContain(r.ProjectAll().Values, p => p.RecipeId == "TEST");
        var akainu = r.Project("590H")!;
        Assert.Equal(4, akainu.IngredientsByAppRawcode.Count(k => k.Key is not "LUMBER" and not "GOLD"));
        Assert.False(akainu.IngredientsByAppRawcode.ContainsKey("R00h"));
        Assert.All(r.ProjectAll().Values, p => Assert.True(p.NoAutomaticEligibilityClaim));
    }
    [Fact] public void SourceAlternativesAndSpecialRequirementsAreNotFabricatedUnits()
    {
        var r = Registry();
        var vegapunk = r.Project("AA0H")!;
        Assert.Equal("TR30", vegapunk.RecipeId);
        Assert.Equal(1, vegapunk.IngredientsByAppRawcode["seraphim_any"]);
        var nika = r.Project("KB0H")!;
        Assert.Contains(nika.ConditionalRequirements, c => c.Source.Id == "H08T" && c.Interpretation.StartsWith("NikaAlternative"));
        Assert.False(nika.IngredientsByAppRawcode.ContainsKey("T80H"));
        Assert.Contains(nika.ConditionalRequirements, c => c.Source.Kind == "ITEM" && c.Source.Id == "AI01");
        Assert.DoesNotContain(r.ProjectAll().Values.SelectMany(p => p.IngredientsByAppRawcode.Keys), k => k.StartsWith("AI"));
        Assert.Contains(vegapunk.ConditionalRequirements, c => c.Source.Kind == "UBAN");
        Assert.Contains(vegapunk.ConditionalRequirements, c => c.Source.Kind == "KING");
        Assert.Null(r.Project("100h"));
        var touched = false;
        Assert.Null(r.Apply("zzzz", _ => touched = true));
        Assert.False(touched);
        foreach (var kind in new[] { "UPUN", "SPEC", "GREN", "RDUN" })
            Assert.DoesNotContain(r.Document.Handlers, h => h.Kind == kind && h.Phase <= 2);
    }
    [Fact] public void SourceFixturesMatchAllRowsAndTestHasNoExternalSourceReference()
    {
        var r = Registry();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, r.Document.SourcePath))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllBytes(Path.Combine(directory!.FullName, r.Document.SourcePath));
        var old = File.ReadAllBytes(Path.Combine(directory.FullName, r.Document.OldSourcePath));
        r.ValidateSource(source, old);
        foreach (var (bytes, rows) in new[] { (source, r.Document.Recipes), (old, r.Document.OldRecipes) })
        {
            var lines = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');
            foreach (var row in rows) Assert.Equal(row.RawSource, string.Join("\n", lines.Skip(row.LineStart - 1).Take(row.LineEnd - row.LineStart + 1)));
        }
        var currentLines = Encoding.UTF8.GetString(source).Replace("\r\n", "\n").Split('\n');
        var test = r.Document.Recipes.Single(r => r.RecipeId == "TEST");
        Assert.DoesNotContain(currentLines.Where((_, index) => index + 1 < test.LineStart || index + 1 > test.LineEnd), line => line.Contains("$54455354", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void PinsAndDispatchEvidenceArePresent()
    {
        var r = Registry();
        Assert.Equal(Map2320RecipeRegistry.SourceSha256, r.Document.SourceSha256);
        Assert.Equal(21, r.Document.Handlers.Length);
        Assert.Equal(264, r.Document.ActiveChoices.Length);
        Assert.Equal(15, r.Document.Recipes.SelectMany(r => r.Conditions).Select(c => c.Kind).Distinct().Count());
        Assert.Contains(r.Document.Evidence, e => e.Name == "EDT" && e.LineStart == 7175 && e.RawSource.Contains("call URb(by,wDT)"));
        Assert.Contains(r.Document.Evidence, e => e.Name == "A12C-spell-nfb" && e.RawSource.Contains("set by=GetSpellAbilityId()"));
        Assert.Throws<InvalidDataException>(() => r.ValidateSource(new byte[] { 1 }, new byte[] { 2 }));
    }
    [Theory]
    [InlineData("schemaVersion")][InlineData("sourceSha256")][InlineData("sourcePath")]
    [InlineData("recipes")][InlineData("activeChoices")][InlineData("handlers")]
    [InlineData("oldRecipes")][InlineData("evidence")][InlineData("changedIds")]
    public void MissingFieldsAreRejected(string field)
    {
        var node = JsonNode.Parse(Registry().Export())!.AsObject(); node.Remove(field);
        Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
    [Theory] [InlineData("count", "0")] [InlineData("count", "-1")] [InlineData("count", "1.5")]
    [InlineData("count", "\"1\"")] [InlineData("id", "\"TEST\"")] [InlineData("kind", "\"NOPE\"")]
    public void MutatedTermsFailClosed(string field, string value)
    {
        var node = JsonNode.Parse(Registry().Export())!;
        node["recipes"]![0]!["conditions"]![0]![field] = JsonNode.Parse(value);
        Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
    [Fact] public void MalformedOversizedDuplicateAndExtraDataFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(new byte[Map2320RecipeRegistry.MaxDataBytes + 1]));
        foreach (var text in new[] { "null", "[]", "{}", "{", "{\"a\":1,\"a\":1}", "{\"x\":true}" })
            Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(Encoding.UTF8.GetBytes(text)));
        var node = JsonNode.Parse(Registry().Export())!; node["unexpected"] = 1;
        Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
        node = JsonNode.Parse(Registry().Export())!;
        node["recipes"]![1] = node["recipes"]![0]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => Map2320RecipeRegistry.Load(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
}
