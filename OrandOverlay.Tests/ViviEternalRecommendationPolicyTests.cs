using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ViviEternalRecommendationPolicyTests
{
    private const string ViviEternal = "rawcode:750h";
    private const string Alchemy = "BestHelp.Alchemy";

    [Fact]
    public void EarlyViviBuildPinsAkainuPathAndRecommendsAlchemy()
    {
        var catalog = Catalog();
        var inventory = Inventory("rawcode:J10h");
        var advice = new NavigationAdvisor(catalog)
            .Evaluate(inventory, catalog.Unit(ViviEternal), round: 10);
        var recommendations = new RecommendationEngine(catalog)
            .RecommendNearestCrafts(ViviEternal, inventory, take: 12,
                navigationMode: Alchemy);

        Assert.Equal(Alchemy, advice[0].OptionId);
        var goal = Assert.Single(recommendations,
            item => item.Route.GoalUnitId == ViviEternal);
        Assert.Contains(goal.Warnings,
            warning => warning.Contains("7강", StringComparison.Ordinal) &&
                       warning.Contains("28", StringComparison.Ordinal));
        var akainu = recommendations.ToList().FindIndex(item =>
            item.Route.GoalUnitId == "rawcode:Z30h");
        Assert.True(akainu is >= 0 and <= 2,
            Render(recommendations));
    }

    [Fact]
    public void CompletedViviDefaultsToSanjiPartnerOnly()
    {
        var recommendations = Engine().RecommendNearestCrafts(
            ViviEternal, Inventory(ViviEternal), take: 12,
            navigationMode: Alchemy);
        var ids = recommendations.Select(item => item.Route.GoalUnitId).ToList();

        Assert.Contains("rawcode:H90H", ids);
        Assert.DoesNotContain("rawcode:4B0H", ids);
        Assert.DoesNotContain("rawcode:R80h", ids);
        Assert.DoesNotContain("rawcode:P30h", ids);
    }

    [Fact]
    public void CompletedViviSwitchesToKidWhenKidPathIsCloser()
    {
        var recommendations = Engine().RecommendNearestCrafts(
            ViviEternal,
            Inventory(ViviEternal, "rawcode:Z90h", "rawcode:540h", "rawcode:L20h"),
            take: 12, navigationMode: Alchemy);
        var ids = recommendations.Select(item => item.Route.GoalUnitId).ToList();

        Assert.Contains("rawcode:4B0H", ids);
        Assert.DoesNotContain("rawcode:H90H", ids);
        var stunners = recommendations.Where(item =>
            item.Route.GoalUnitId != "rawcode:4B0H" &&
            GoalStrategyCalculator.StrategyMetricsFor(
                Catalog().Unit(item.Route.GoalUnitId)).Stun > 0).ToList();
        Assert.True(stunners.Count == 0, Render(stunners));
    }

    [Fact]
    public void CompletedViviOrdersTokiKikuAndMobyDickCore()
    {
        var recommendations = Engine().RecommendNearestCrafts(
            ViviEternal,
            Inventory(ViviEternal, "rawcode:H90H", "rawcode:060h", "rawcode:Y50h"),
            take: 12,
            navigationMode: Alchemy);
        var ids = recommendations.Select(item => item.Route.GoalUnitId).ToList();

        var toki = ids.IndexOf("rawcode:780h");
        var kiku = ids.IndexOf("rawcode:640h");
        var mobyDick = ids.IndexOf("mobydick");
        Assert.True(toki >= 0 && kiku >= 0 && mobyDick >= 0,
            Render(recommendations));
        Assert.True(toki < kiku && kiku < mobyDick,
            Render(recommendations));
    }

    private static RecommendationEngine Engine()
    {
        var catalog = Catalog();
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

    private static InventoryEntry[] Inventory(params string[] unitIds) =>
        unitIds.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new InventoryEntry
            {
                UnitId = group.Key,
                Count = group.Count()
            })
            .ToArray();

    private static string Render(IEnumerable<Recommendation> recommendations) =>
        string.Join(" > ", recommendations.Select(item =>
            $"{item.Route.GoalUnitId}:{item.Route.Name}"));
}
