using OrandOverlay;
using System.Globalization;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CompletedGoalRecommendationTests
{
    [Fact]
    public void MidMatchStartupDoesNotTreatUnknownRoundAsFirstRareQuest()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var inventory = new[]
        {
            new InventoryEntry { UnitId = "rawcode:H00h", Count = 25 }
        };
        var targetRares = new RecommendationEngine(catalog)
            .RecipeRareUnitIds("rawcode:E90H");
        var gate = new FirstRareRecommendationGate();

        Assert.False(gate.ShouldPrioritize(
            "rawcode:E90H", inventory, targetRares, currentRound: 0));
        var recommendations = new RecommendationEngine(catalog).RecommendNearestCrafts(
            "rawcode:E90H", inventory, take: 8, suppressFirstRareShip: true);
        Assert.DoesNotContain(recommendations, recommendation =>
            recommendation.ProgressionGoalUnitId == "rawcode:E90H");
    }

    [Fact]
    public void DoflamingoFieldFormMapsToCompletedTranscendentGoal()
    {
        Assert.True(RawcodeCodec.TryParse("D90H", out var fieldForm));
        Assert.Equal("rawcode:E90H", RawcodeCodec.DynamicUnitId(fieldForm));

        var catalog = new DataCatalog();
        catalog.Load();
        var recommendations = new RecommendationEngine(catalog).RecommendNearestCrafts(
            "rawcode:E90H",
            [new InventoryEntry { UnitId = RawcodeCodec.DynamicUnitId(fieldForm), Count = 1 }],
            take: 8);

        Assert.DoesNotContain(recommendations, recommendation =>
            recommendation.ProgressionGoalUnitId == "rawcode:E90H");
    }

    [Fact]
    public void OwnedDoflamingoDoesNotRestartItsBabyFiveProgression()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var recommendations = new RecommendationEngine(catalog).RecommendNearestCrafts(
            "rawcode:E90H",
            [new InventoryEntry { UnitId = "rawcode:E90H", Count = 1 }],
            take: 8);

        Assert.DoesNotContain(recommendations, recommendation =>
            recommendation.ProgressionGoalUnitId == "rawcode:E90H");
        Assert.DoesNotContain(recommendations, recommendation =>
            recommendation.Route.GoalUnitId == "rawcode:M20h");
    }

    [Fact]
    public void OwnedDoflamingoWithOnlyPointNineStunGetsStunSupportFirst()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var finalFailedHand = FailedHand();

        var clearStats = ClearBuildStats.Load(
            [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")]);
        var engine = new RecommendationEngine(catalog, clearStats);
        var startingMetrics = engine.AggregateStrategyMetrics(finalFailedHand.ToDictionary(
            entry => entry.UnitId, entry => entry.Count, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(212, startingMetrics.ArmorReduction);
        var recommendations = engine.RecommendNearestCrafts(
            "rawcode:E90H", finalFailedHand, take: 8,
            navigationMode: "PathOfKings.BountyHunter", gorosei: GoroseiMode.Saturn);

        Assert.True(Stun(catalog.Unit(recommendations[0].Route.GoalUnitId)) > 0,
            $"첫 추천 {recommendations[0].Route.Name}에 스턴이 없습니다.");
        var armorFinishers = recommendations.Skip(1)
            .Where(recommendation =>
                Armor(catalog.Unit(recommendation.Route.GoalUnitId)) > 0)
            .ToList();
        Assert.True(armorFinishers.Count == 0,
            "이미 방깎 211을 넘겼는데 추가 방깎이 남았습니다: " + string.Join(" > ",
                recommendations.Select(recommendation => recommendation.Route.Name)));
    }

    [Fact]
    public void DoflamingoBelowArmorTargetGetsCheapestCompletingSetWithinBoardLimit()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var clearStats = ClearBuildStats.Load(
            [Path.Combine(AppContext.BaseDirectory, "Data", "tmo-clear-samples.json")]);
        var handWithoutHawk = FailedHand()
            .Where(entry => entry.UnitId != "rawcode:3A0h")
            .ToList();

        var engine = new RecommendationEngine(catalog, clearStats);
        var recommendations = engine
            .RecommendNearestCrafts("rawcode:E90H", handWithoutHawk, take: 8,
                navigationMode: "PathOfKings.BountyHunter", gorosei: GoroseiMode.Saturn);
        var stunSupports = recommendations
            .Where(recommendation => Stun(catalog.Unit(
                recommendation.Route.GoalUnitId)) > 0)
            .ToList();
        var armorFinishers = recommendations
            .Where(recommendation => Armor(catalog.Unit(
                recommendation.Route.GoalUnitId)) > 0 &&
                Stun(catalog.Unit(recommendation.Route.GoalUnitId)) <= 0)
            .ToList();

        var order = string.Join(" > ",
            recommendations.Select(recommendation => recommendation.Route.Name));
        Assert.True(stunSupports.Count == 1, "스턴 후보가 1기가 아닙니다: " + order);
        Assert.NotEmpty(armorFinishers);
        Assert.True(recommendations.Count <= 12, "추천 보드가 12칸을 넘었습니다: " + order);
        var projectedBuild = handWithoutHawk
            .Concat(recommendations.Select(recommendation => new InventoryEntry
            {
                UnitId = recommendation.Route.GoalUnitId,
                Count = 1
            }))
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var projectedArmor = engine.AggregateStrategyMetrics(projectedBuild).ArmorReduction;
        Assert.True(projectedArmor >= GoalStrategyCalculator.FullArmorReductionTarget,
            $"방깎 코어 미달 {projectedArmor:0}: {order}");
    }

    [Fact]
    public void JinbeAndVergoConditionalArmorReductionIsCountedTogether()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var engine = new RecommendationEngine(catalog);
        var metrics = engine.AggregateStrategyMetrics(new Dictionary<string, int>
        {
            ["rawcode:G30h"] = 1,
            ["rawcode:W30h"] = 1
        });

        Assert.Equal(55, metrics.ArmorReduction);
        Assert.Equal(1, metrics.ArmorBreak);
        // This assertion exercises the historical catalog-only conditional rule, not TMO 48784.
        var displayed = new InventoryStatsCalculator(catalog,
            HandStatsProfile.CreateForTests("catalog-only legacy conditional fixture", [])).Calculate(
            [Entry("rawcode:G30h"), Entry("rawcode:W30h")]);
        Assert.Equal(55, displayed.TotalArmorReduction);
        Assert.Equal(1, displayed.ArmorBreakProviders);
    }

    private static InventoryEntry Entry(string unitId, int count = 1) =>
        new() { UnitId = unitId, Count = count };

    private static InventoryEntry[] FailedHand() =>
    [
        Entry("rawcode:200h"), Entry("luffy_common", 3), Entry("rawcode:3A0h"),
        Entry("rawcode:540h"), Entry("rawcode:600h", 2), Entry("rawcode:620h"),
        Entry("rawcode:700h"), Entry("rawcode:710h"), Entry("rawcode:800h", 6),
        Entry("rawcode:900h", 5), Entry("rawcode:D00h"), Entry("rawcode:E90H"),
        Entry("rawcode:G20h"), Entry("rawcode:G30h"), Entry("rawcode:H20h"),
        Entry("rawcode:J00h"), Entry("rawcode:M00h"), Entry("rawcode:N20h"),
        Entry("rawcode:V50h"), Entry("dragon_legend"), Entry("rawcode:W30h")
    ];

    private static double Stun(UnitDefinition unit) =>
        unit.OfficialAbilities
            .Where(ability => ability.Name == "스턴")
            .Sum(ability => double.TryParse(ability.DisplayValue, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ? Math.Abs(value) : 0);

    private static double Armor(UnitDefinition unit) =>
        unit.OfficialAbilities
            .Where(ability => ability.Name is "방어력 감소" or "발동방어력 감소" or
                "중첩방어력 감소")
            .Sum(ability => double.TryParse(ability.DisplayValue, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ? Math.Abs(value) : 0);
}
