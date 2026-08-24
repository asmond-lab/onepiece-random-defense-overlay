using System.Globalization;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class PhysicalTopRecommendationPolicyTests
{
    [Theory]
    [InlineData("yamato_transcendent")]
    [InlineData("rawcode:B90H")]
    [InlineData("rawcode:A90H")]
    [InlineData("rawcode:F90H")]
    public void EveryPhysicalTopUsesMinimumCorePolicy(string goalUnitId)
    {
        var catalog = Catalog();
        var profile = GoalStrategyCalculator.StrategyProfileFor(catalog.Unit(goalUnitId));

        Assert.True(profile is
        {
            PrioritizeStunRecommendations: true,
            MinimizeArmorRecommendationSet: true,
            StopAfterCoreTargets: true
        });
    }

    [Fact]
    public void OwnedYamatoGetsStableStunBeforeCommunitySupports()
    {
        var catalog = Catalog();
        var recommendations = Engine(catalog).RecommendNearestCrafts(
            "yamato_transcendent", [Entry("yamato_transcendent")], take: 8,
            navigationMode: "PathOfKings.BountyHunter");

        Assert.NotEmpty(recommendations);
        Assert.True(Stun(catalog.Unit(recommendations[0].Route.GoalUnitId)) > 0,
            $"첫 추천 {recommendations[0].Route.Name}에 스턴이 없습니다.");
    }

    [Fact]
    public void UsoppAboveArmorTargetStopsArmorRecommendations()
    {
        var catalog = Catalog();
        var inventory = new[]
        {
            Entry("rawcode:B90H"), Entry("rawcode:V50h"), Entry("rawcode:3A0h"),
            Entry("rawcode:W30h"), Entry("rawcode:G30h"), Entry("dragon_legend"),
            Entry("rawcode:O30h"), Entry("rawcode:HA0h"), Entry("rawcode:V20h"),
            Entry("rawcode:N20h")
        };
        var engine = Engine(catalog);
        Assert.Equal(216, engine.AggregateStrategyMetrics(inventory.ToDictionary(
            entry => entry.UnitId, entry => entry.Count, StringComparer.OrdinalIgnoreCase))
            .ArmorReduction);

        var recommendations = engine.RecommendNearestCrafts(
            "rawcode:B90H", inventory, take: 8,
            navigationMode: "PathOfKings.BountyHunter");
        var armorFinishers = recommendations.Where(recommendation =>
                Armor(catalog.Unit(recommendation.Route.GoalUnitId)) > 0)
            .ToList();

        Assert.Empty(armorFinishers);
    }

    [Fact]
    public void MagicTopKeepsItsExistingCommunityPolicy()
    {
        var catalog = Catalog();
        var profile = GoalStrategyCalculator.StrategyProfileFor(
            catalog.Unit("rawcode:H90H"));

        Assert.True(profile is
        {
            FillCommunitySupports: true,
            PrioritizeStunRecommendations: false,
            MinimizeArmorRecommendationSet: false,
            StopAfterCoreTargets: false
        });
    }

    private static DataCatalog Catalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }

    private static RecommendationEngine Engine(DataCatalog catalog)
    {
        var stats = ClearBuildStats.Load(
            [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")]);
        return new RecommendationEngine(catalog, stats);
    }

    private static InventoryEntry Entry(string unitId) =>
        new() { UnitId = unitId, Count = 1 };

    private static double Stun(UnitDefinition unit) =>
        Value(unit, "스턴");

    private static double Armor(UnitDefinition unit) =>
        Value(unit, "방어력 감소", "발동방어력 감소", "중첩방어력 감소");

    private static double Value(UnitDefinition unit, params string[] names) =>
        unit.OfficialAbilities
            .Where(ability => names.Contains(ability.Name, StringComparer.Ordinal))
            .Sum(ability => double.TryParse(ability.DisplayValue, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ? Math.Abs(value) : 0);
}
