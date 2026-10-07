using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322GrowthActivityTests
{
    private static string SourcePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "artifacts", "ordr-2322", "map-extracted", "war3map.j"));

    [Fact]
    public void GrowthDeclarationsAndActiveRecipesMatchIndependentPinnedJass()
    {
        var bytes = File.ReadAllBytes(SourcePath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal("6805a1def612d237ba0046a5000f8a2514e928445c8071ecb734b874fa39b47d", hash);
        var lines = Encoding.UTF8.GetString(bytes).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var growth = Map2320GrowthSource.LoadBundled("2.322");
        Assert.Equal(3926, growth.Globals.Count);
        Assert.Equal("yl", growth.GrowthName);
        Assert.Equal("GR", growth.PreviousName);
        Assert.Equal("hR", growth.NextName);
        Assert.Equal("Vs", growth.TimerName);
        var end = Array.IndexOf(lines, "endglobals");
        Assert.Equal(growth.Globals.Count, end - 1);
        foreach (var line in lines.Skip(1).Take(end - 1))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"^(\w+)( array)? (\w+)(?:=.*)?$");
            Assert.True(match.Success, line);
            var tag = match.Groups[2].Success ? match.Groups[1].Value switch
            {
                "integer" => 9, "real" => 10, "string" => 11, "boolean" => 13, _ => 12
            } : match.Groups[1].Value switch
            {
                "integer" => 4, "real" => 5, "string" => 6, "boolean" => 8, "code" => 3, _ => 7
            };
            Assert.Equal(tag, growth.Globals[match.Groups[3].Value]);
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Data", "map-activity-2322.json")));
        var root = document.RootElement;
        Assert.Equal(hash, root.GetProperty("jassSha256").GetString());
        Assert.Equal(265, root.GetProperty("recipes").GetArrayLength());
        var choices = root.GetProperty("recipes").EnumerateArray().ToArray();
        foreach (var row in choices)
        {
            var id = row.GetProperty("id").GetString()!;
            var sourceLine = row.GetProperty("sourceLine").GetInt32();
            var hex = Convert.ToHexString(Encoding.ASCII.GetBytes(id)).ToLowerInvariant();
            Assert.Contains("call SaveBoolean(VR,$" + hex + ",1,true)", lines[sourceLine - 1], StringComparison.OrdinalIgnoreCase);
            var choiceLine = row.GetProperty("activeChoiceSourceLine").GetInt32();
            Assert.Contains("$" + hex, lines[choiceLine - 1], StringComparison.OrdinalIgnoreCase);
        }
        var rules = Map2321ActivityRules.LoadBundled("2.322");
        Assert.Equal("2.322", rules.MapVersion);
        Assert.Equal(18, rules.IntegerArrays.Count);
        Assert.All(root.GetProperty("integerArrays").EnumerateArray(), row =>
        {
            var name = row.GetProperty("name").GetString()!;
            Assert.Equal(9, growth.Globals[name]);
            foreach (var line in row.GetProperty("sourceLines").EnumerateArray())
                Assert.Contains(name, lines[line.GetInt32() - 1]);
        });
        Assert.DoesNotContain(rules.Recipes, recipe => recipe.Id == "A0QG");
        Assert.Contains(choices, row => row.GetProperty("id").GetString() == "A0QG" &&
            !row.GetProperty("isActiveChoice").GetBoolean());
    }

    [Fact]
    public void SourcesAreVersionedAndTamperingIsRejectedWithoutChanging2321()
    {
        var growth = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "map-growth-globals-2322.json"));
        var activity = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "map-activity-2322.json"));
        Assert.Equal("2.322", Map2320GrowthSource.Load(growth, "2.322").MapVersion);
        Assert.Equal("2.322", Map2321ActivityRules.Load(activity, "2.322").MapVersion);
        Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load(growth, "2.321"));
        Assert.Throws<InvalidDataException>(() => Map2321ActivityRules.Load(activity, "2.321"));
        growth[growth.Length / 2] ^= 1;
        activity[activity.Length / 2] ^= 1;
        Assert.Throws<InvalidDataException>(() => Map2320GrowthSource.Load(growth, "2.322"));
        Assert.Throws<InvalidDataException>(() => Map2321ActivityRules.Load(activity, "2.322"));
        Assert.Equal("Vu", Map2320GrowthSource.LoadBundled("2.321").GrowthName);
        Assert.Equal(19, Map2321ActivityRules.LoadBundled().IntegerArrays.Count);
    }
}
