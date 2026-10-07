using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2322KaidoAirRoleTests
{
    private static UnitDefinition Kaido(string id) => new()
    {
        Id = id, Name = "Kaido", Tier = "불멸", Rawcodes = ["M70h"],
        OfficialAbilities = [new UnitAbilityDisplay { Name = "공중이동", DisplayValue = "가능" }],
        Recipe = new Dictionary<string, int> { ["ingredient"] = 1 }
    };

    [Fact]
    public void ObservedDragonFlightsRemainCreditedWhileEnhancedHybridIsNot()
    {
        var dragon = Kaido("rawcode:DA0h");
        var hybrid = Kaido("rawcode:WB0h");
        Assert.Equal(Map2322KaidoMovement.FlyingDragon,
            Map2322KaidoAirRole.Decide("2.322", dragon.Id, dragon.Rawcodes, traitEnhanced: true));
        Assert.Equal(Map2322KaidoMovement.GroundHybrid,
            Map2322KaidoAirRole.Decide("2.322", hybrid.Id, hybrid.Rawcodes, traitEnhanced: true));
        Assert.Equal(1, GoalStrategyCalculator.StrategyMetricsFor(dragon, "2.322").AirMovement);
        Assert.Equal(0, GoalStrategyCalculator.StrategyMetricsFor(hybrid, "2.322").AirMovement);
    }

    [Fact]
    public void CanonicalKaidoIsConditionalNotAnUnconditionalAirProvider()
    {
        var canonical = Kaido("rawcode:M70h");
        Assert.Equal(Map2322KaidoMovement.Conditional,
            Map2322KaidoAirRole.Decide("2.322", canonical.Id, canonical.Rawcodes));
        Assert.Equal(Map2322KaidoMovement.EnhancedFormUnknown,
            Map2322KaidoAirRole.Decide("2.322", canonical.Id, canonical.Rawcodes, traitEnhanced: true));
        Assert.False(Map2322KaidoAirRole.CountsAsAir("2.322", canonical, true));
        Assert.Equal(0, GoalStrategyCalculator.StrategyMetricsFor(canonical, "2.322").AirMovement);
        Assert.Contains("미확인", Map2322KaidoAirRole.ConditionalNote);
    }

    [Theory]
    [InlineData("2.314")]
    [InlineData("2.320")]
    [InlineData("2.321")]
    public void HistoricalMapsKeepExistingGuideCredit(string version)
    {
        var kaido = Kaido("rawcode:M70h");
        Assert.True(Map2322KaidoAirRole.CountsAsAir(version, kaido, true));
        Assert.Equal(1, GoalStrategyCalculator.StrategyMetricsFor(kaido, version).AirMovement);
    }

    [Fact]
    public void NormalGuideCategoriesAndObservedCoverageRespectKnownForm()
    {
        var baseUnit = Kaido("rawcode:M70h");
        var dragon = Kaido("rawcode:DA0h");
        var hybrid = Kaido("rawcode:WB0h");
        var guide = new[] { new NormalGuideUnit(baseUnit.Id, false, "physical", ["공중이동"]),
            new NormalGuideUnit(hybrid.Id, false, "physical", ["공중이동"]) };
        var browser = new NormalCandidateBrowser([baseUnit, dragon, hybrid,
            new UnitDefinition { Id = "ingredient", Name = "ingredient" }], null, guide, "2.322");
        browser.Update(new Dictionary<string, int> { ["ingredient"] = 1, [baseUnit.Id] = 1,
            [hybrid.Id] = 1 }, true, 1);
        Assert.False(browser.MovementCoverage.Single(c => c.Role == "공중이동").Covered);
        Assert.Contains(Map2322KaidoAirRole.ConditionalNote, browser.Snapshot.Status);
        browser.SetStage(NormalCandidateStage.Utility);
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "공중이동").Candidates,
            c => c.Unit.Id == dragon.Id);
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(g => g.Candidates),
            c => c.Unit.Id == hybrid.Id && c.MovementRoles.Contains("공중이동"));
        browser.Update(new Dictionary<string, int> { [dragon.Id] = 1 }, true, 1);
        Assert.True(browser.MovementCoverage.Single(c => c.Role == "공중이동").Covered);
    }
}
