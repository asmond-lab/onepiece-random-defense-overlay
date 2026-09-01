using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AlchemyDismantleAdvisorTests
{
    private const string Vivi = "rawcode:750h";
    private const string Alchemy = "BestHelp.Alchemy";

    [Fact]
    public void OverlayShowsOnlyActionableDismantleAdvice()
    {
        var advice = new[]
        {
            new SpecialDismantleAdvice("keep", "로브 루치", false, "핵심 재료"),
            new SpecialDismantleAdvice("break", "마가렛", true, "경로 밖")
        };

        var visible = OverlayWindow.DismantleOnly(advice);

        var item = Assert.Single(visible);
        Assert.Equal("break", item.UnitId);
        Assert.True(item.Dismantle);
    }

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
    public void UnfinishedViviDismantlesFutureExpertSpecials()
    {
        var catalog = Catalog();
        var inventory = Hand("rawcode:S00h", "rawcode:310h");
        var recommendations = Engine(catalog).RecommendNearestCrafts(
            Vivi, inventory, take: 12, navigationMode: Alchemy);

        var advice = new AlchemyDismantleAdvisor(catalog).Evaluate(
            inventory, recommendations, catalog.Unit(Vivi), Alchemy, []);

        Assert.False(Advice(advice, "rawcode:S00h").Dismantle);
        Assert.True(Advice(advice, "rawcode:310h").Dismantle);
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
    public void CompletedViviProtectsOnlyImmediateExpertSpecialsAndTheirNeededCount()
    {
        var catalog = Catalog();
        var inventory = Hand(Vivi, "rawcode:310h", "rawcode:310h", "rawcode:B00h");
        var recommendations = Engine(catalog).RecommendNearestCrafts(
            Vivi, inventory, take: 12, navigationMode: Alchemy);

        var immediateExpert = recommendations.First(item =>
            item.Route.GoalUnitId is "rawcode:H90H" or "rawcode:4B0H" or
                "rawcode:780h" or "rawcode:640h" or "mobydick");
        var advice = new AlchemyDismantleAdvisor(catalog).Evaluate(
            inventory, recommendations, catalog.Unit(Vivi), Alchemy, []);

        Assert.Equal("rawcode:H90H", immediateExpert.Route.GoalUnitId);
        Assert.True(Advice(advice, "rawcode:310h").Dismantle);
        Assert.Contains("초과 1기", Advice(advice, "rawcode:310h").Reason);
        Assert.True(Advice(advice, "rawcode:B00h").Dismantle);
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
