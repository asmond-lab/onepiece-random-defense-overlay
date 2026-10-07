using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class EffectiveStrategyRegressionTests
{
    [Theory]
    [InlineData("신", false)]
    [InlineData("악몽", true)]
    public void SupportBoardStopsArmorAtTheSameDifficultyFloor(string difficulty, bool needsArmor)
    {
        var catalog = Catalog();
        // Keep the established 216-armor fixture, but lower one owned contribution by 11.
        var originalArmor = catalog.Unit("rawcode:V20h").OfficialAbilities
            .Where(ability => ability.Name == "방어력 감소")
            .Sum(ability => double.Parse(ability.DisplayValue, System.Globalization.CultureInfo.InvariantCulture));
        catalog.RawcodeCatalog["V20h"].Abilities["방어력 감소"] =
            System.Text.Json.JsonSerializer.SerializeToElement(originalArmor - 11);
        var inventory = new[]
        {
            Entry("rawcode:B90H"), Entry("rawcode:V50h"), Entry("rawcode:3A0h"),
            Entry("rawcode:W30h"), Entry("rawcode:G30h"), Entry("dragon_legend"),
            Entry("rawcode:O30h"), Entry("rawcode:HA0h"), Entry("rawcode:V20h"),
            Entry("rawcode:N20h")
        };
        var engine = new RecommendationEngine(catalog);
        Assert.Equal(205, engine.AggregateStrategyMetrics(
            inventory.ToDictionary(entry => entry.UnitId, entry => entry.Count)).ArmorReduction);
        var cards = engine.RecommendNearestCrafts("rawcode:B90H", inventory, take: 8,
            navigationMode: "PathOfKings.BountyHunter", difficulty: difficulty);
        Assert.Equal(needsArmor, cards.Any(card =>
            GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(card.Route.GoalUnitId)).ArmorReduction > 0));
    }

    [Fact]
    public void NikaNoSlowReadinessMatchesEngineStunAndSlowTargets()
    {
        var catalog = Catalog();
        var engine = new RecommendationEngine(catalog);

        var cards = engine.RecommendNearestCrafts(
            "rawcode:KB0H", [], take: 8, buildVariant: "noslow");

        Assert.Equal(2.9, engine.ActiveStunTarget, 4);
        Assert.Equal(40, engine.ActiveSlowTarget, 4);
        Assert.NotEmpty(cards);
        var readiness = cards[0].CombatReadiness!;
        Assert.Equal(2.9, readiness.RequiredStun, 4);
        Assert.Equal(40, readiness.RequiredSlow, 4);
        var kpi = OverlayWindow.ResolveCoreKpiTargets(
            readiness, GoroseiMode.None, engine.ActiveStunTarget);
        Assert.Equal(2.9, kpi.Stun, 4);
        Assert.Equal(40, kpi.Slow, 4);
    }

    [Fact]
    public void DivineArmorPolicyIsSharedByReadinessCurrentCraftAndKpi()
    {
        var catalog = Catalog();
        var engine = new RecommendationEngine(catalog);
        var inventory = new[] { new InventoryEntry { UnitId = "yamato_transcendent", Count = 1 } };

        var cards = engine.RecommendNearestCrafts(
            "yamato_transcendent", inventory, take: 4, difficulty: "신", round: 1);

        Assert.Equal(201d, engine.ActiveArmorReductionTarget);
        Assert.NotEmpty(cards);
        Assert.All(cards, card =>
        {
            Assert.Equal(201d, card.CombatReadiness!.RequiredArmorReduction);
            if (card.CurrentCraft is { } craft)
                Assert.Equal(201d, craft.Strategy.ArmorReductionTarget);
        });
        var kpi = OverlayWindow.ResolveCoreKpiTargets(
            cards[0].CombatReadiness, GoroseiMode.None, engine.ActiveStunTarget);
        Assert.Equal(201d, kpi.Armor);
    }

    [Fact]
    public void NasjuroAndWarcuryUseTheSameEffectiveProfileAsTheEngine()
    {
        var catalog = Catalog();
        var nasjuroEngine = new RecommendationEngine(catalog);
        var nasjuro = nasjuroEngine.RecommendNearestCrafts(
            "yamato_transcendent", [], take: 1, gorosei: GoroseiMode.Nasjuro);
        Assert.Equal(112d, nasjuroEngine.ActiveSlowTarget);
        Assert.Equal(112d, nasjuro[0].CombatReadiness!.RequiredSlow);
        Assert.Equal(112d, OverlayWindow.ResolveCoreKpiTargets(
            nasjuro[0].CombatReadiness, GoroseiMode.Nasjuro, nasjuroEngine.ActiveStunTarget).Slow);

        var warcuryEngine = new RecommendationEngine(catalog);
        var warcury = warcuryEngine.RecommendNearestCrafts(
            "yamato_transcendent", [], take: 1, gorosei: GoroseiMode.Warcury);
        Assert.Equal(221d, warcuryEngine.ActiveArmorReductionTarget);
        Assert.Equal(221d, warcury[0].CombatReadiness!.RequiredArmorReduction);
        Assert.Equal(221d, OverlayWindow.ResolveCoreKpiTargets(
            warcury[0].CombatReadiness, GoroseiMode.Warcury, warcuryEngine.ActiveStunTarget).Armor);
    }

    [Fact]
    public void TacticalStunRestoreIsVisibleOnReadinessAndKpi()
    {
        var catalog = Catalog();
        var engine = new RecommendationEngine(catalog);
        var inventory = new[]
        {
            Entry("rawcode:T10h"), Entry("rawcode:M10h"), Entry("rawcode:G10h"),
            Entry("rawcode:X50h"), Entry("greenblood_buff"), Entry("rawcode:B20h")
        };

        var cards = engine.RecommendNearestCrafts(
            "rawcode:F40h", inventory, take: 12,
            navigationMode: "PathOfKings.BountyHunter");

        Assert.Equal(1.4, engine.ActiveStunTarget, 4);
        Assert.Equal(1.4, cards[0].CombatReadiness!.RequiredStun, 4);
        Assert.Equal(1.4, OverlayWindow.ResolveCoreKpiTargets(
            cards[0].CombatReadiness, GoroseiMode.None, engine.ActiveStunTarget).Stun, 4);
    }

    [Fact]
    public void MagicWarcuryKeepsOneSourceAndMagnitudeTen()
    {
        var catalog = Catalog();
        var engine = new RecommendationEngine(catalog);
        var goal = catalog.AllUnits.First(unit =>
            GoalStrategyCalculator.IsMagicDamageTier(unit.Tier) &&
            TopGradePolicy.IsTopGrade(unit.Tier));

        var cards = engine.RecommendNearestCrafts(
            goal.Id, [], take: 1, gorosei: GoroseiMode.Warcury);

        var readiness = cards[0].CombatReadiness!;
        Assert.Equal(ReadinessDamageType.Magic, readiness.DamageType);
        Assert.Equal(1, readiness.RequiredMagicArmorSources);
        Assert.Equal(10d, engine.ActiveMagicArmorReductionTarget);
        Assert.Equal(10d, readiness.RequiredMagicArmorReduction);
        var kpi = OverlayWindow.ResolveCoreKpiTargets(
            readiness, GoroseiMode.Warcury, engine.ActiveStunTarget);
        Assert.Equal(10d, kpi.MagicArmor);
        Assert.NotEqual(readiness.RequiredMagicArmorSources, kpi.MagicArmor);
    }

    private static DataCatalog Catalog()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        return catalog;
    }

    private static InventoryEntry Entry(string unitId) =>
        new() { UnitId = unitId, Count = 1 };
}
