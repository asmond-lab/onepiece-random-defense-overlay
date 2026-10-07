using Xunit;

namespace OrandOverlay.Tests;

public sealed class HandStatsBundledDataTests
{
    private readonly DataCatalog _catalog;
    private readonly HandStatsProfile _profile;

    public HandStatsBundledDataTests()
    {
        _catalog = new DataCatalog();
        _catalog.Load(loadCarryPolicy: false);
        _profile = HandStatsProfile.LoadBundled() ?? throw new InvalidOperationException("48784 profile missing");
    }

    [Fact]
    public void AllBundledCodesExistAndSourceCoverageIsExplicit()
    {
        Assert.Equal(240, _profile.Units.Count);
        Assert.Single(_profile.Unmapped);
        foreach (var unit in _profile.Units)
            Assert.True(_catalog.RawcodeCatalog.ContainsKey(unit.Rawcode), unit.Rawcode);
    }

    [Fact]
    public void SpecialChopperUsesScreenSevenWithoutChangingCatalog()
    {
        var before = _catalog.Unit("rawcode:D10h").OfficialAbilities
            .Select(ability => (ability.Name, ability.DisplayValue)).ToArray();
        var stats = Calculate(("D10h", 2));
        Assert.Equal(14, stats.AttackBoost);
        Assert.Equal(before, _catalog.Unit("rawcode:D10h").OfficialAbilities
            .Select(ability => (ability.Name, ability.DisplayValue)).ToArray());
    }

    [Fact]
    public void SeparateNonStackingFamiliesDoNotSuppressEachOther()
    {
        var stats = Calculate(("610h", 2), ("U10h", 1), ("O30h", 1), ("Y30h", 1));
        Assert.Equal(21, stats.ArmorReduction);
    }

    [Fact]
    public void WhitebeardBossStoryBonusIsNotOrdinaryArmor()
    {
        var stats = Calculate(("B30h", 1));
        Assert.Equal(15, stats.ArmorReduction);
        Assert.Equal(0, stats.TriggeredArmorReduction);
        Assert.Equal(15, stats.TotalArmorReduction);
        Assert.Contains("30", stats.SourceNotes);
    }

    [Fact]
    public void GabanAndYoumuSingleArmorDoesNotBecomeAreaArmor()
    {
        var stats = Calculate(("F40h", 1), ("CC0h", 1));
        Assert.Equal(60, stats.SingleArmorReduction);
        Assert.Equal(0, stats.TotalArmorReduction);
    }

    [Fact]
    public void BonneyStackMaximumDoesNotSatisfyArmorTarget()
    {
        var stats = Calculate(("XB0H", 1));
        Assert.Equal(40, stats.UnobservedStackingArmorReduction);
        Assert.Equal(0, stats.TotalArmorReduction);
    }

    [Fact]
    public void ConflictingChopperBuffIsNotSilentlySelected()
    {
        var stats = Calculate(("K20h", 1));
        Assert.Equal(0, stats.AttackBoost);
        Assert.True(stats.UnknownValueUnitCount > 0);
        Assert.Contains("25", stats.SourceNotes);
        Assert.Contains("30", stats.SourceNotes);
    }

    [Fact]
    public void SengokuFormAndBaseAreSeparateSourceRows()
    {
        Assert.True(_profile.TryGet("E40h", out var normal));
        Assert.True(_profile.TryGet("MB0h", out var enhanced));
        Assert.True(normal.TryGetNumber("스턴", out var normalStun));
        Assert.True(enhanced.TryGetNumber("스턴", out var enhancedStun));
        Assert.Equal(1.1, normalStun);
        Assert.Equal(1.8, enhancedStun);
        Assert.Equal(1.1, Calculate(("E40h", 1)).Stun);
        Assert.Equal(1.8, Calculate(("MB0h", 1)).Stun);
    }

    private InventoryStatSummary Calculate(params (string Code, int Count)[] units) =>
        new InventoryStatsCalculator(_catalog, _profile).Calculate(units.Select(unit =>
            new InventoryEntry { UnitId = "rawcode:" + unit.Code, Count = unit.Count }));
}
