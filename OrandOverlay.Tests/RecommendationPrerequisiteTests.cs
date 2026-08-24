using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RecommendationPrerequisiteTests
{
    [Fact]
    public void MihawkWithOnlyOneOfTwoRequiredShipsKeepsMissingShipWarning()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var engine = new RecommendationEngine(catalog);

        var oneShip = engine.RecommendNearestCrafts(
            "rawcode:850h", [Entry("rawcode:060h", 1)], take: 1)[0];
        var twoShips = engine.RecommendNearestCrafts(
            "rawcode:850h", [Entry("rawcode:060h", 2)], take: 1)[0];

        Assert.Contains(oneShip.MissingSpecials,
            warning => warning.Contains("해적선", StringComparison.Ordinal));
        Assert.DoesNotContain(twoShips.MissingSpecials,
            warning => warning.Contains("해적선", StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateProgressCacheInvalidatesWhenInventoryChanges()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var reused = new RecommendationEngine(catalog);
        reused.RecommendNearestCrafts("yamato_transcendent",
            [Entry("luffy_common", 1)]);

        var actual = reused.RecommendNearestCrafts("yamato_transcendent", []);
        var expected = new RecommendationEngine(catalog)
            .RecommendNearestCrafts("yamato_transcendent", []);

        Assert.Equal(Render(expected), Render(actual));
    }

    [Fact]
    public void MihawkReservesBothRequiredShips()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var engine = new RecommendationEngine(catalog);

        var requiredShips = engine.RequiredShipCodes(
            catalog.Unit("rawcode:850h"),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rawcode:060h"] = 2
            });

        Assert.Equal(2, requiredShips.Count(code =>
            code.Equals("060h", StringComparison.Ordinal)));
    }

    private static string Render(IEnumerable<Recommendation> recommendations) =>
        string.Join("|", recommendations.Select(recommendation =>
            $"{recommendation.Route.GoalUnitId}:" +
            $"{recommendation.RecipeProgress.OwnedLeafCount}/" +
            $"{recommendation.RecipeProgress.RequiredLeafCount}"));

    private static InventoryEntry Entry(string unitId, int count) =>
        new() { UnitId = unitId, Count = count };
}
