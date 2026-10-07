using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideSupportTests
{
    private readonly DataCatalog _catalog = new();
    public BulletGuideSupportTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData("E10h")]
    [InlineData("610h")]
    [InlineData("H20h")]
    [InlineData("D20h")]
    [InlineData("K50h")]
    public void AuxiliaryFillersAreNotConstructionGoalsBeforeBullet(string code)
    {
        var inventory = _catalog.Unit("rawcode:" + code).Recipe
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (code is "H20h" or "D20h" or "K50h")
            foreach (var armor in new[] { "H30h", "N30h", "S30h", "830h", "Z20h" })
                inventory["rawcode:" + armor] = 1;
        var support = new BulletGuideSupportPolicy(_catalog).Evaluate(inventory, null, GoroseiMode.None,
            id => id == "rawcode:" + code);
        Assert.Null(support.RecommendedUnitId);
    }

    [Fact]
    public void ReadySmallArmorRecipeDoesNotOutrankPrimarySupportAfterBullet()
    {
        var inventory = _catalog.Unit("rawcode:E10h").Recipe.ToDictionary(pair => pair.Key, pair => pair.Value);
        inventory[BulletGuidePolicy.GoalId] = 1;
        inventory["rawcode:Z20h"] = 1;
        var support = new BulletGuideSupportPolicy(_catalog).Evaluate(inventory, null, GoroseiMode.None,
            id => id is "rawcode:E10h" or "rawcode:M30h");
        Assert.Equal("rawcode:M30h", support.RecommendedUnitId);
    }

    [Fact]
    public void SmallArmorCanFillLeftoversAfterBulletAndPrimaryStun()
    {
        var inventory = new Dictionary<string, int> { [BulletGuidePolicy.GoalId] = 1, ["rawcode:Z20h"] = 1 };
        var support = new BulletGuideSupportPolicy(_catalog).Evaluate(inventory, null, GoroseiMode.None,
            id => id == "rawcode:E10h");
        Assert.Equal("rawcode:E10h", support.RecommendedUnitId);
    }

    [Fact]
    public void SharedBuffNoGainIsNotArmorProgress()
    {
        var support = Evaluate(("180h", 1), ("U30h", 1), ("540h", 1), ("O30h", 1),
            ("A20h", 1), ("H20h", 1), ("510h", 1));
        Assert.True(support.ArmorPotential < support.ArmorTarget);
        Assert.NotEqual("rawcode:Y30h", support.RecommendedUnitId);
        Assert.NotNull(support.RecommendedUnitId);
    }

    [Fact]
    public void SupportPlanSkipsReadyQueenThatConsumesLastMobileUnit()
    {
        var inventory = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Q30h", "K50h",
            "HA0h", "I20h", "L00h" }.ToDictionary(code => "rawcode:" + code, _ => 1);
        Assert.False(BulletGuideCraftSafety.Allows(_catalog, "rawcode:IC0h", inventory, 20,
            BulletGuidePolicy.NavigationId));
        var plan = new BulletGuidePolicy(_catalog).Plan(20, 13, inventory, "악몽", BulletGuidePolicy.NavigationId);
        Assert.NotEqual("rawcode:IC0h", plan.TargetUnitId);
        Assert.NotNull(plan.TargetUnitId);
    }

    [Fact]
    public void ReadyQueenCompletesOwnedBonClayPairWithoutInventingControl()
    {
        var inventory = new[] { "180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Q30h", "K50h",
            "HA0h", "I20h", "L00h" }.ToDictionary(code => "rawcode:" + code, _ => 1);
        var support = new BulletGuideSupportPolicy(_catalog).Evaluate(inventory,
            BulletGuidePolicy.NavigationId, GoroseiMode.None);
        Assert.False(support.StunPairReady);
        Assert.Equal("rawcode:IC0h", support.RecommendedUnitId);
    }

    [Fact]
    public void SameBuffUsesMaximumNotSumAndDuplicatesDoNotIncreaseIt()
    {
        var support = Evaluate(("D20h", 2), ("Y00h", 1), ("H20h", 1), ("A10h", 1), ("O30h", 1), ("Y30h", 1));
        Assert.Equal(30, support.SlowPotential);
        Assert.Equal(11, support.ArmorPotential);
    }

    [Fact]
    public void BulletComponentsCannotSupplyPostCraftControl()
    {
        var support = Evaluate(("930h", 1), ("V20h", 1), ("U20h", 1));
        Assert.Equal(0, support.SlowPotential);
        Assert.False(support.StunPairReady);
    }

    [Fact]
    public void OwnedBulletDoesNotCreateUnobservedUpgradeContributions()
    {
        var support = Evaluate(("180h", 1));
        Assert.Equal(0, support.ArmorPotential);
        Assert.Equal(0, support.SlowPotential);
    }

    [Fact]
    public void BountySlowIsCountedOnlyFromConfirmedNavigation()
    {
        var inventory = new Dictionary<string, int> { ["rawcode:Q30h"] = 1, ["rawcode:M30h"] = 1 };
        var policy = new BulletGuideSupportPolicy(_catalog);
        Assert.Equal(65, policy.Evaluate(inventory, null, GoroseiMode.None).SlowPotential);
        Assert.Equal(72, policy.Evaluate(inventory, BulletGuidePolicy.NavigationId, GoroseiMode.None).SlowPotential);
    }

    [Fact]
    public void WarcuryAddsGuideTwentyTargetNotClaimedActualDebuff()
    {
        var support = new BulletGuideSupportPolicy(_catalog).Evaluate(new Dictionary<string, int>(), null, GoroseiMode.Warcury);
        Assert.Equal(120, support.ArmorTarget);
        Assert.Equal(0, support.ArmorPotential);
    }

    [Fact]
    public void ActualMapKarugaraAndRedForceArmorAreUsed()
    {
        var support = Evaluate(("F30h", 1), ("U30h", 1), ("K30h", 1));
        Assert.Equal(58, support.ArmorPotential);
    }

    [Fact]
    public void SourceStunPairIsACompositionConditionNotFictionalSeconds()
    {
        var support = Evaluate(("IC0h", 1), ("O30h", 1));
        Assert.True(support.StunPairReady);
    }

    private BulletGuideSupport Evaluate(params (string Code, int Count)[] items) =>
        new BulletGuideSupportPolicy(_catalog).Evaluate(items.ToDictionary(item => "rawcode:" + item.Code, item => item.Count),
            null, GoroseiMode.None);
}
