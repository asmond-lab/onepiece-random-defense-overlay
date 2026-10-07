using System.Text.Json;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class TmoRecipeAuthorityTests
{
    private readonly DataCatalog _catalog = LoadCatalog();

    [Theory]
    [InlineData("590H", "J10h,MC0h,R00h,S40h,Z30h")]
    [InlineData("KB0H", "700I,990H,C30h,D20h,K00h")]
    [InlineData("CB0h", "030h,G60h,HA0h,W60h")]
    [InlineData("Q90h", "K30h,T20h,Y00I")]
    [InlineData("U40h", "N10h,S00h,X20h")]
    [InlineData("390H", "190H")]
    [InlineData("G90H", "H90H")]
    [InlineData("MB0h", "E40h")]
    [InlineData("TB0H", "300I,F90H")]
    [InlineData("MA0H", "AA0H")]
    [InlineData("BA0H", "AA0H")]
    [InlineData("EA0H", "AA0H")]
    [InlineData("3A0h", "340h,item_greenblood")]
    [InlineData("0A0h", "G30h,item_greenblood")]
    [InlineData("Y90h", "230h,item_greenblood")]
    [InlineData("1A0h", "030h,item_greenblood")]
    public void CorrectedRecipesMatchTmo42479(string resultRawcode, string expectedCsv)
    {
        var expected = expectedCsv.Split(',')
            .ToHashSet(StringComparer.Ordinal);

        var actual = RecipeIngredientKeys(resultRawcode);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("3A0h")]
    [InlineData("0A0h")]
    [InlineData("Y90h")]
    [InlineData("1A0h")]
    public void AnySeraphimSatisfiesVegapunkRecipe(string seraphimRawcode)
    {
        var engine = new RecommendationEngine(_catalog);
        var inventory = new[]
        {
            Entry(seraphimRawcode),
            Entry("550h"),
            Entry("R10h"),
            Entry("S40h")
        };

        var recommendation = engine
            .RecommendNearestCrafts("rawcode:AA0H", inventory)
            .First(item => item.Route.GoalUnitId == "rawcode:AA0H");

        Assert.Equal(1, recommendation.RecipeProgress.CompletionRatio);
    }

    [Fact]
    public void AuditMetadataCoversEveryTmo42479Recipe()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data",
            "tmo-recipe-source.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal(303, root.GetProperty("capturedUnitCount").GetInt32());
        Assert.Equal(252, root.GetProperty("capturedRecipeCardCount").GetInt32());
        Assert.Equal(249, root.GetProperty("canonicalRecipeUnitCount").GetInt32());
        Assert.Equal(249, root.GetProperty("matchedRecipeUnitCount").GetInt32());
        Assert.Equal(
            "d14aa983b40b6eeb2fc2435b893a41fc2aaa198468ee49c0637b5d5034a784e3",
            root.GetProperty("normalizedRecipeSha256").GetString());
    }

    private HashSet<string> RecipeIngredientKeys(string resultRawcode) =>
        _catalog.Unit("rawcode:" + resultRawcode).Recipe.Keys
            .Select(_catalog.Unit)
            .Where(unit => unit.Tier != "자원")
            .Select(unit => unit.Rawcodes.FirstOrDefault() ?? unit.Id)
            .Where(id => id is not "GOLD" and not "LUMBER" and not "POINT"
                and not "RANDOM")
            .ToHashSet(StringComparer.Ordinal);

    private static InventoryEntry Entry(string rawcode) =>
        new() { UnitId = "rawcode:" + rawcode, Count = 1 };

    private static DataCatalog LoadCatalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }
}
