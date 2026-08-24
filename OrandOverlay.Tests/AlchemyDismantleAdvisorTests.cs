using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AlchemyDismantleAdvisorTests
{
    private const string Vivi = "rawcode:750h";
    private const string Alchemy = "BestHelp.Alchemy";

    [Fact]
    public void ViviAlchemyKeepsGoalSpecialsAndDismantlesOffPathSpecials()
    {
        var catalog = Catalog();
        var inventory = Hand("rawcode:S00h", "rawcode:R00h");
        var recommendations = Engine(catalog).RecommendNearestCrafts(
            Vivi, inventory, take: 12, navigationMode: Alchemy);

        var advice = new AlchemyDismantleAdvisor(catalog).Evaluate(
            inventory, recommendations, catalog.Unit(Vivi), Alchemy, []);

        Assert.False(Advice(advice, "rawcode:S00h").Dismantle);
        Assert.True(Advice(advice, "rawcode:R00h").Dismantle);
    }

    [Fact]
    public void KidBranchKeepsBrainPointAndDismantlesSanjiOnlySpecial()
    {
        var catalog = Catalog();
        var inventory = Hand(Vivi, "rawcode:Z90h", "rawcode:540h",
            "rawcode:U00h", "rawcode:Y00h", "rawcode:E10h", "rawcode:P00h");
        var recommendations = Engine(catalog).RecommendNearestCrafts(
            Vivi, inventory, take: 12, navigationMode: Alchemy);

        var advice = new AlchemyDismantleAdvisor(catalog).Evaluate(
            inventory, recommendations, catalog.Unit(Vivi), Alchemy, []);

        Assert.False(Advice(advice, "rawcode:E10h").Dismantle);
        Assert.True(Advice(advice, "rawcode:P00h").Dismantle);
    }

    [Fact]
    public void GrowthSpecialIsNeverRecommendedForAlchemyDismantle()
    {
        var catalog = Catalog();
        var inventory = Hand(Vivi, "rawcode:V00h");
        var recommendations = Engine(catalog).RecommendNearestCrafts(
            Vivi, inventory, take: 12, navigationMode: Alchemy);

        var advice = new AlchemyDismantleAdvisor(catalog).Evaluate(
            inventory, recommendations, catalog.Unit(Vivi), Alchemy,
            ["rawcode:V00h"]);

        Assert.False(Advice(advice, "rawcode:V00h").Dismantle);
        Assert.Contains("성장형", Advice(advice, "rawcode:V00h").Reason);
    }

    private static SpecialDismantleAdvice Advice(
        IEnumerable<SpecialDismantleAdvice> advice, string unitId) =>
        Assert.Single(advice, item => item.UnitId == unitId);

    private static RecommendationEngine Engine(DataCatalog catalog)
    {
        var stats = ClearBuildStats.Load(
            [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")]);
        return new RecommendationEngine(catalog, stats);
    }

    private static DataCatalog Catalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }

    private static InventoryEntry[] Hand(params string[] ids) => ids
        .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
        .Select(group => new InventoryEntry { UnitId = group.Key, Count = group.Count() })
        .ToArray();
}
