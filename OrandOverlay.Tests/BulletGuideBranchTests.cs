using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideBranchTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("R50h", false)]
    [InlineData("H50h", false)]
    [InlineData("O50h", false)]
    [InlineData("Q50h", false)]
    [InlineData("840h", true)]
    public void ReadyBruleePreparationUsesActualMissionExceptions(string? exception, bool prepare)
    {
        var inventory = new[] { "HA0h", "MC0h", "C20h", "O10h", "X50h", "010h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        if (exception is not null) inventory["rawcode:" + exception] = 1;
        var plan = new BulletGuidePolicy(_catalog).Plan(18, 9, inventory, "악몽");
        Assert.Equal(prepare, plan.TargetUnitId == "rawcode:S80h");
    }

    private readonly DataCatalog _catalog = new();
    public BulletGuideBranchTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(true, 29, true)]
    [InlineData(false, 29, false)]
    [InlineData(true, 30, false)]
    public void ActiveDestructionRacePrioritizesReadyFourthLegend(bool active, int round, bool priority)
    {
        var inventory = new[] { "930h", "V20h", "U30h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        foreach (var ingredient in _catalog.Unit("rawcode:U20h").Recipe)
            if (_catalog.Unit(ingredient.Key).Tier != "자원")
                inventory[ingredient.Key] = ingredient.Value;
        var plan = new BulletGuidePolicy(_catalog).Plan(round, 10, inventory, "악몽",
            destructionKingAvailable: active);
        Assert.Equal(priority, plan.Stage == BulletGuideStage.DestructionRace);
        if (priority || round >= 30) Assert.Equal("rawcode:U20h", plan.TargetUnitId);
    }

    [Theory]
    [InlineData(true, 29, 10, true)]
    [InlineData(true, 30, 10, false)]
    [InlineData(true, 29, 11, false)]
    [InlineData(false, 29, 10, false)]
    [InlineData(null, 29, 10, false)]
    public void DestructionRaceRequiresObservedMissionAndUnexpiredStory(bool? active, int round, int story, bool pursue)
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(round, story, new Dictionary<string, int>(), "악몽",
            destructionKingAvailable: active);
        Assert.Equal(pursue, plan.PursueDestructionKing);
    }

    [Fact]
    public void KingRedMissionRaceConsidersSafeReadyFourthBeforeUnreadyShiki()
    {
        var inventory = new[] { "HA0h", "U30h", "MC0h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        foreach (var ingredient in _catalog.Unit("rawcode:H30h").Recipe)
            if (_catalog.Unit(ingredient.Key).Tier != "자원") inventory[ingredient.Key] = ingredient.Value;
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, "rawcode:H30h", inventory, 29, null));
        var plan = new BulletGuidePolicy(_catalog).Plan(29, 10, inventory, "악몽", destructionKingAvailable: true);
        Assert.Equal(BulletGuideStage.DestructionRace, plan.Stage);
        Assert.Equal("rawcode:H30h", plan.TargetUnitId);
    }

    [Fact]
    public void DestructionRaceConsumesLastAuxiliaryChopperForReadyFourthLegend()
    {
        var inventory = new[] { "HA0h", "U30h", "MC0h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        foreach (var ingredient in _catalog.Unit("rawcode:S30h").Recipe)
            if (_catalog.Unit(ingredient.Key).Tier != "자원") inventory[ingredient.Key] = ingredient.Value;
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, "rawcode:S30h", inventory, 29, null));
        var plan = new BulletGuidePolicy(_catalog).Plan(29, 10, inventory, "악몽", destructionKingAvailable: true);
        Assert.Equal(BulletGuideStage.DestructionRace, plan.Stage);
        Assert.Equal("rawcode:S30h", plan.TargetUnitId);
    }

    [Fact]
    public void AuxiliaryChopperDoesNotRequireFirstLegendExemption()
    {
        var inventory = _catalog.Unit("rawcode:S30h").Recipe
            .Where(pair => _catalog.Unit(pair.Key).Tier != "자원")
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var plan = new BulletGuidePolicy(_catalog).Plan(10, 3, inventory, "악몽");
        Assert.Equal(BulletGuideStage.FirstLegend, plan.Stage);
        Assert.Equal("rawcode:S30h", plan.TargetUnitId);
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, plan.TargetUnitId!, inventory, 10, null, plan: plan));
        Assert.True(BulletGuideCraftSafety.Allows(_catalog, plan.TargetUnitId!, inventory, 10, null,
            plan: plan with { Stage = BulletGuideStage.SecondLegend, KnownLegendLowerBound = 1 }));
    }

    [Fact]
    public void PreparedBruleeIsNotTheThirdCombatLegend()
    {
        var inventory = new[] { "HA0h", "MC0h", "S80h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        var plan = new BulletGuidePolicy(_catalog).Plan(18, 9, inventory, "악몽");
        Assert.Equal(BulletGuideStage.AirFoundation, plan.Stage);
        Assert.Equal("rawcode:930h", plan.TargetUnitId);
    }

    [Fact]
    public void PreviouslyObservedLegendDoesNotResetOpeningAfterTransformation()
    {
        var inventory = new Dictionary<string, int>
        {
            ["rawcode:HA0h"] = 1, ["luffy_common"] = 1
        };
        var plan = new BulletGuidePolicy(_catalog).Plan(18, 9, inventory, "악몽",
            observedLegendIds: ["rawcode:HA0h", "rawcode:MC0h"]);
        Assert.Equal(BulletGuideStage.AirFoundation, plan.Stage);
        Assert.Equal("rawcode:930h", plan.TargetUnitId);
    }

    [Theory]
    [InlineData(20, false, false)]
    [InlineData(30, false, true)]
    [InlineData(40, false, true)]
    [InlineData(30, true, false)]
    public void SpareCommonsPermitOneSourceChopperSupplement(int commons, bool chopper, bool expected)
    {
        var inventory = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        inventory["luffy_common"] = commons;
        if (chopper) inventory["rawcode:K20h"] = 1;
        var plan = new BulletGuidePolicy(_catalog).Plan(50, 13, inventory, "악몽",
            BulletGuidePolicy.NavigationId);
        Assert.Equal(expected, plan.TargetUnitId == "rawcode:K20h");
    }

    [Fact]
    public void SaturnAddsRareChopperAfterRequiredFormationIsReady()
    {
        var inventory = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h" }
            .ToDictionary(code => "rawcode:" + code, _ => 1);
        var plan = new BulletGuidePolicy(_catalog).Plan(60, 13, inventory, "악몽",
            BulletGuidePolicy.NavigationId, GoroseiMode.Saturn);
        Assert.Equal("rawcode:K20h", plan.TargetUnitId);
        inventory["rawcode:K20h"] = 1;
        var fulfilled = new BulletGuidePolicy(_catalog).Plan(60, 13, inventory, "악몽",
            BulletGuidePolicy.NavigationId, GoroseiMode.Saturn);
        Assert.Null(fulfilled.TargetUnitId);
    }

    [Fact]
    public void KingAndRedUseSourceShikiSmokerBlackbeardTieOrder()
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(20, 9,
            new Dictionary<string, int> { ["rawcode:HA0h"] = 1, ["rawcode:U30h"] = 1 }, "악몽");
        Assert.Equal("rawcode:930h", plan.TargetUnitId);
    }

    [Fact]
    public void RedAndKarugaraRespectOnePrimaryBossExceptionWithoutInventingTwo()
    {
        var plan = new BulletGuidePolicy(_catalog).Plan(50, 13,
            new Dictionary<string, int> { ["rawcode:180h"] = 1, ["rawcode:U30h"] = 1, ["rawcode:F30h"] = 1 }, "악몽");
        Assert.Equal(1, plan.BossCount);
        Assert.NotEqual(BulletGuideStage.BossSupport, plan.Stage);
    }
}
