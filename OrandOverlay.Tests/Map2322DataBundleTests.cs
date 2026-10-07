using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322DataBundleTests
{
    [Fact]
    public void BundledBehaviorKeepsUnknownFailureAndConditionalRecipe()
    {
        var bundle = Map2322DataBundle.LoadBundled();
        Assert.Equal(64, bundle.Fingerprint.Length);
        var recipe = Assert.IsType<Map2320RecipeProjection>(bundle.Recipes.Project("2C0h"));
        Assert.Equal(265, bundle.Recipes.ProjectAll().Count);
        Assert.Equal("A12C", bundle.Recipes.Project("CB0h")!.RecipeId);
        Assert.Equal("A00D", bundle.Recipes.Project("K00h")!.RecipeId);
        Assert.Null(bundle.Recipes.Project("XXXX"));
        Assert.DoesNotContain(bundle.Recipes.ProjectAll().Values, row => row.RecipeId == "TEST");
        Assert.Equal(81, bundle.Commands.Count);
        Assert.Equal(new[] { "아카이누조합", "akainu" }, bundle.Commands["Z30h"]);
        Assert.False(bundle.Commands.ContainsKey("2C0h"));
        Assert.Equal(156, bundle.Hotkeys.Count);
        var yujiroHotkey = Assert.Single(bundle.Hotkeys, x => x.Result == "2C0h");
        Assert.Equal("Z", yujiroHotkey.Key);
        Assert.Contains("T60h", yujiroHotkey.HostRawcodes);
        Assert.Equal(14, bundle.Story.Stages.Count);
        Assert.Equal(("n000", 180), (bundle.Story.Stages[0].ObjectiveRawcode, bundle.Story.Stages[0].BaseGold));
        Assert.Contains(bundle.Story.Stages[0].BaseUnits, x => x.Id == "e018" && x.Count == 3);
        Assert.Contains(bundle.Story.Stages[0].DirectPayoutOperations, x => x.Branch == "OtherSourceBranch" && x.Statement.Contains("call G1f("));
        Assert.Equal(("n009", 5000), (bundle.Story.Stages[13].ObjectiveRawcode, bundle.Story.Stages[13].BaseGold));
        Assert.Contains(bundle.Story.Stages[6].DirectPayoutOperations, x => x.Branch == "OtherSourceBranch" && x.Kind == "Resource");
        Assert.Contains(bundle.Story.Stages[8].BaseUnits, x => x.Id == "h05Y" && x.Count == 1);
        Assert.Equal(15, bundle.Navigation.Options.Count);
        Assert.Contains(bundle.Navigation.Options, x => x.Id == "AlliedForces.DoubleBenefit" && x.HandlerSource.Contains("일석이조"));
        Assert.Contains(bundle.Navigation.Options, x => x.Id == "PathOfKings.MartialLaw" && x.HandlerSource.Contains("계엄령"));
        Assert.Equal(7, bundle.Navigation.Exchanges.Count);
        Assert.Equal(2, Assert.Single(bundle.Navigation.Exchanges, x => x.Ability == "A0JR").Cost);
        Assert.Equal(6, Assert.Single(bundle.Navigation.Exchanges, x => x.Ability == "A0BV").Cost);
        Assert.Equal("A0QG", recipe.RecipeId);
        Assert.Equal(1, recipe.IngredientsByAppRawcode["T60h"]);
        Assert.Equal(1, recipe.IngredientsByAppRawcode["Y20h"]);
        Assert.Equal(1, recipe.IngredientsByAppRawcode["Y30h"]);
        Assert.Equal(1, recipe.IngredientsByAppRawcode["G60h"]);
        Assert.Equal(10000, recipe.IngredientsByAppRawcode["GOLD"]);
        Assert.Equal(7, recipe.IngredientsByAppRawcode["LUMBER"]);
        Assert.Equal(new[] { "KING:h0C2", "PICK:A800" }, recipe.ConditionalRequirements.Select(x => x.Source.Kind + ":" + x.Source.Id));
        Assert.True(recipe.HasUnresolvedRequirements);
        Assert.Equal(35, Enumerable.Range(1, 100).Count(roll => bundle.Navigation.SelectionWispsForDoubleWispRoll(roll) == 2));
        Assert.Equal(65, Enumerable.Range(1, 100).Count(roll => bundle.Navigation.SelectionWispsForDoubleWispRoll(roll) is null));
        Assert.Equal("UninitializedJassLocal", bundle.Navigation.DoubleWispFailureStatus);
        Assert.Equal(5, Enumerable.Range(1, 10).Count(roll => bundle.Navigation.BoxForRoll(roll).Kind == "Gold"));
        Assert.Equal(4, Enumerable.Range(1, 10).Count(roll => bundle.Navigation.BoxForRoll(roll).Kind == "RandomWisp"));
        Assert.Equal(1, Enumerable.Range(1, 10).Count(roll => bundle.Navigation.BoxForRoll(roll).Kind == "SelectionWisp"));
        Assert.Equal(5000, bundle.Navigation.BoxForRoll(10).Amount);
        Assert.Equal(4, bundle.Story.ClearBerry(nightmare: true, zeroUnitCount: true, allThreeMissionsComplete: true));
        Assert.Equal(2, bundle.Story.ClearBerry(nightmare: false, zeroUnitCount: true, allThreeMissionsComplete: true));
        Assert.Equal(1, bundle.Story.PoneglyphImmediateBerry(true));
        Assert.Equal(25, Enumerable.Range(1, 1000).Count(roll => bundle.Story.TreasureBerryForRoll(roll) == 1));
    }

    public static IEnumerable<object[]> DatasetFiles() => Map2322DataBundle.ExpectedMembers.Select(x => new object[] { x.File });

    [Theory]
    [MemberData(nameof(DatasetFiles))]
    public void EveryMemberTamperFailsClosed(string file)
    {
        using var sandbox = new Sandbox();
        File.AppendAllText(Path.Combine(sandbox.Directory, file), "x", Encoding.UTF8);
        Assert.Throws<InvalidDataException>(() => Map2322DataBundle.LoadFromDirectory(sandbox.Directory));
    }

    [Fact]
    public void TamperedMemberAndMixedVersionManifestFailClosed()
    {
        using var sandbox = new Sandbox();
        var recipePath = Path.Combine(sandbox.Directory, "map-recipes-2322.json");
        File.AppendAllText(recipePath, "x", Encoding.UTF8);
        Assert.Throws<InvalidDataException>(() => Map2322DataBundle.LoadFromDirectory(sandbox.Directory));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "map-recipes-2322.json"), recipePath, true);
        var manifestPath = Path.Combine(sandbox.Directory, "map-source-manifest-2322.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        manifest["MapVersion"] = "2.320";
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        Assert.Throws<InvalidDataException>(() => Map2322DataBundle.LoadFromDirectory(sandbox.Directory));
    }

    private sealed class Sandbox : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "map2322-" + Guid.NewGuid().ToString("N"));
        public Sandbox()
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var name in Map2322DataBundle.ExpectedMembers.Select(x => x.File).Append("map-source-manifest-2322.json"))
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", name), Path.Combine(Directory, name));
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
