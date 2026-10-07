using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CombatReadinessTests
{
    [Theory]
    [InlineData(210.9, 102, 1.4, false)]
    [InlineData(211, 101.9, 1.4, false)]
    [InlineData(211, 102, 1.39, false)]
    [InlineData(211, 102, 1.4, true)]
    [InlineData(240, 120, 1.5, true)]
    public void PhysicalReadinessUsesCoreThresholdBoundaries(
        double armor, double slow, double stun, bool expected)
    {
        var goal = TestUnit("physical", "초월 [물딜]");
        var readiness = CombatReadinessCalculator.FromMetrics(
            goal,
            new GoalStrategyProfile(0, 0),
            new StrategyMetrics(Slow: slow, Stun: stun,
                ArmorReduction: armor),
            magicSourceCount: 0);

        Assert.Equal(expected, readiness.IsReady);
    }

    [Theory]
    [InlineData("신", 200.9, 201, false)]
    [InlineData("신", 201, 201, true)]
    [InlineData("악몽", 210.9, 211, false)]
    [InlineData("악몽", 211, 211, true)]
    [InlineData("unknown", 201, 211, false)]
    public void PhysicalArmorTargetUsesRecognizedDifficulty(
        string difficulty, double armor, double expectedTarget, bool expectedReady)
    {
        var readiness = CombatReadinessCalculator.FromMetrics(
            TestUnit("physical", "초월 [물딜]"),
            new GoalStrategyProfile(0, 0),
            new StrategyMetrics(Slow: 102, Stun: 1.4,
                ArmorReduction: armor),
            magicSourceCount: 0,
            difficulty: difficulty);

        Assert.Equal(expectedTarget, readiness.RequiredArmorReduction);
        Assert.Equal(expectedReady, readiness.IsReady);
    }

    [Fact]
    public void RecommendationEngineCarriesRecognizedDifficultyIntoReadiness()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var engine = new RecommendationEngine(catalog);

        var divine = engine.RecommendNearestCrafts(
            "yamato_transcendent", [], take: 1, difficulty: "신");
        var nightmare = engine.RecommendNearestCrafts(
            "yamato_transcendent", [], take: 1, difficulty: "악몽");

        Assert.All(divine, recommendation =>
            Assert.Equal(201d, recommendation.CombatReadiness!.RequiredArmorReduction));
        Assert.All(nightmare, recommendation =>
            Assert.Equal(211d, recommendation.CombatReadiness!.RequiredArmorReduction));
    }

    [Theory]
    [InlineData(0, 102, 1.4, false)]
    [InlineData(1, 101.9, 1.4, false)]
    [InlineData(1, 102, 1.39, false)]
    [InlineData(1, 102, 1.4, true)]
    public void MagicReadinessRequiresOneDistinctSourceAndCoreControl(
        int sources, double slow, double stun, bool expected)
    {
        var goal = TestUnit("magic", "초월 [마딜]");
        var readiness = CombatReadinessCalculator.FromMetrics(
            goal,
            new GoalStrategyProfile(0, 0, MagicArmorReductionTarget: 1),
            new StrategyMetrics(Slow: slow, Stun: stun),
            sources);

        Assert.Equal(expected, readiness.IsReady);
        Assert.Equal(Math.Max(0, 1 - sources),
            readiness.MissingMagicArmorSource ? 1 : 0);
    }

    [Fact]
    public void DuplicateInventoryCopiesCountAsOneMagicSource()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var source = catalog.AllUnits.First(unit =>
            GoalStrategyCalculator.StrategyMetricsFor(unit)
                .MagicArmorReduction > 0);
        var goal = catalog.AllUnits.First(unit =>
            GoalStrategyCalculator.IsMagicDamageTier(unit.Tier) &&
            TopGradePolicy.IsTopGrade(unit.Tier));

        var readiness = CombatReadinessCalculator.Calculate(catalog, goal,
        [
            new InventoryEntry { UnitId = source.Id, Count = 2 },
            new InventoryEntry { UnitId = source.Id, Count = 1, IsManual = true }
        ]);

        Assert.Equal(1, readiness.CurrentMagicArmorSources);
    }

    private static UnitDefinition TestUnit(string id, string tier) =>
        new()
        {
            Id = id,
            Name = id,
            Tier = tier
        };
}
