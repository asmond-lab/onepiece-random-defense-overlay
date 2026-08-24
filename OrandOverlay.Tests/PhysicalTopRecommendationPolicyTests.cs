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

    [Fact]
    public void GarpFirstBoardCompletesStableStunAndFullArmor()
    {
        var catalog = Catalog();
        var engine = Engine(catalog);
        var recommendations = engine.RecommendNearestCrafts(
            "rawcode:C40h", [], take: 8,
            navigationMode: "PathOfKings.BountyHunter", gorosei: GoroseiMode.Saturn);
        var storyIngredients = engine.RecipeLegendaryUnitIds("rawcode:C40h");
        var build = recommendations
            .Where(recommendation => !storyIngredients.Contains(
                recommendation.Route.GoalUnitId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(recommendation => recommendation.Route.GoalUnitId,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var metrics = engine.AggregateStrategyMetrics(build);
        var order = string.Join(" > ",
            recommendations.Select(recommendation => recommendation.Route.Name));

        Assert.True(metrics.Stun >= 1.4,
            $"거프 첫 보드 스턴 {metrics.Stun:0.0}: {order}");
        Assert.True(metrics.ArmorReduction >= 211,
            $"거프 첫 보드 방깎 {metrics.ArmorReduction:0}: {order}");
        Assert.True(metrics.Slow >= 102,
            $"거프 첫 보드 이감 {metrics.Slow:0}: {order}");
    }

    [Fact]
    public void CloserUsefulArmorPathPrecedesDistantHigherArmorPath()
    {
        var catalog = Catalog();
        var engine = Engine(catalog);
        var kalgaraId = "rawcode:F30h";
        var kalgaraRares = engine.RecipeRareUnitIds(kalgaraId);
        Assert.True(kalgaraRares.Count >= 3);
        var lastRareSpecials = engine.RecipeSpecialUnitIds(kalgaraRares[^1]);
        Assert.True(lastRareSpecials.Count >= 2);
        var inventory = kalgaraRares.Take(kalgaraRares.Count - 1)
            .Concat(lastRareSpecials)
            .Select(Entry)
            .Append(Entry("rawcode:C40h"))
            .ToList();
        var directKalgara = engine.RecommendNearestCrafts(kalgaraId, inventory, take: 1)
            .First(recommendation => recommendation.Route.GoalUnitId.Equals(kalgaraId,
                StringComparison.OrdinalIgnoreCase));

        var recommendations = engine.RecommendNearestCrafts(
            "rawcode:C40h", inventory, take: 8,
            navigationMode: "PathOfKings.BountyHunter", gorosei: GoroseiMode.Saturn)
            .ToList();
        var ryokugyuIndex = recommendations.FindIndex(recommendation =>
            catalog.Unit(recommendation.Route.GoalUnitId).Rawcodes.Contains(
                "N30h", StringComparer.Ordinal));
        var order = string.Join(" > ",
            recommendations.Select(recommendation => recommendation.Route.Name));

        Assert.True(directKalgara.RecipeProgress.CompletionRatio >= 0.9,
            $"카르가라 직접 완성도 부족: {directKalgara.RecipeProgress.CompletionRatio:P0}");
        var beforeRyokugyu = ryokugyuIndex < 0
            ? recommendations
            : recommendations.Take(ryokugyuIndex).ToList();
        var hasCloserArmorPath = beforeRyokugyu
            .Where(recommendation =>
                Armor(catalog.Unit(recommendation.Route.GoalUnitId)) > 0)
            .Any(recommendation => engine.RecommendNearestCrafts(
                    recommendation.Route.GoalUnitId, inventory, take: 1)
                .First().RecipeProgress.CompletionRatio >= 0.9);
        Assert.True(hasCloserArmorPath,
            $"완성 임박 방깎 경로가 먼 료쿠규보다 먼저여야 합니다: {order}");
    }

    [Theory]
    [InlineData("PathOfKings.BountyHunter", false)]
    [InlineData("PathOfKings.BountyHunter", true)]
    [InlineData("Gambler.ContinuousBetting", false)]
    [InlineData("Gambler.ContinuousBetting", true)]
    public void DoflamingoNavigationDoesNotRecommendMagicUnit(
        string navigationMode, bool ownsGoal)
    {
        var catalog = Catalog();
        var doflamingo = catalog.Unit("rawcode:E90H");
        Assert.Contains("[물딜]", doflamingo.Tier, StringComparison.Ordinal);
        IReadOnlyList<InventoryEntry> inventory = ownsGoal
            ? [Entry("rawcode:E90H")]
            : [];

        var recommendations = Engine(catalog).RecommendNearestCrafts(
            "rawcode:E90H", inventory, take: 12,
            navigationMode: navigationMode);

        Assert.DoesNotContain(recommendations, recommendation =>
        {
            var unit = catalog.Unit(recommendation.Route.GoalUnitId);
            return unit.Rawcodes.Contains("130h", StringComparer.Ordinal) ||
                   unit.Tier.Contains("[마딜]", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DoflamingoDoesNotRecommendReadyFujitoraAsStunSupport()
    {
        var catalog = Catalog();
        var fujitora = catalog.Unit("rawcode:130h");
        var readyFujitora = fujitora.Recipe
            .Select(pair => new InventoryEntry
            {
                UnitId = pair.Key,
                Count = pair.Value
            })
            .Append(Entry("rawcode:E90H"))
            .ToList();
        var aokiji = catalog.AllUnits.First(unit =>
            unit.Name.Equals("아오키지", StringComparison.Ordinal) &&
            Math.Abs(Stun(unit) - 0.2) < 0.0001);

        var recommendations = Engine(catalog).RecommendNearestCrafts(
            "rawcode:E90H", readyFujitora.Append(Entry(aokiji.Id)).ToList(),
            take: 12, navigationMode: "PathOfKings.BountyHunter");

        Assert.DoesNotContain(recommendations, recommendation =>
            catalog.Unit(recommendation.Route.GoalUnitId).Rawcodes
                .Contains("130h", StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("yamato_transcendent")]
    [InlineData("rawcode:B90H")]
    [InlineData("rawcode:A90H")]
    [InlineData("rawcode:F90H")]
    [InlineData("rawcode:E90H")]
    [InlineData("rawcode:C40h")]
    public void PhysicalGoalRejectsFujitoraAsFinishedSupport(string goalUnitId)
    {
        var catalog = Catalog();

        Assert.False(GoalStrategyCalculator.IsCompatibleSupportDamageType(
            catalog.Unit(goalUnitId), catalog.Unit("rawcode:130h")));
    }

    [Theory]
    [InlineData("yamato_transcendent")]
    [InlineData("rawcode:B90H")]
    [InlineData("rawcode:A90H")]
    [InlineData("rawcode:F90H")]
    public void OwnedPhysicalTopBoardCompletesConfiguredCoreTargets(string goalUnitId)
    {
        var catalog = Catalog();
        var engine = Engine(catalog);
        var recommendations = engine.RecommendNearestCrafts(
            goalUnitId, [Entry(goalUnitId)], take: 8,
            navigationMode: "PathOfKings.BountyHunter");
        var build = recommendations
            .Select(recommendation => recommendation.Route.GoalUnitId)
            .Append(goalUnitId)
            .GroupBy(unitId => unitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var metrics = engine.AggregateStrategyMetrics(build);
        var profile = Assert.IsType<GoalStrategyProfile>(
            GoalStrategyCalculator.StrategyProfileFor(catalog.Unit(goalUnitId)));
        var order = string.Join(" > ",
            recommendations.Select(recommendation => recommendation.Route.Name));

        Assert.True(metrics.Stun + 0.0001 >= profile.StunTarget,
            $"{goalUnitId} 스턴 {metrics.Stun:0.0}/{profile.StunTarget:0.0}: {order}");
        Assert.True(metrics.ArmorReduction + 0.0001 >= profile.ArmorReductionTarget,
            $"{goalUnitId} 방깎 {metrics.ArmorReduction:0}/{profile.ArmorReductionTarget:0}: {order}");
        Assert.True(metrics.Slow + 0.0001 >= profile.SlowTarget,
            $"{goalUnitId} 이감 {metrics.Slow:0}/{profile.SlowTarget:0}: {order}");
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
