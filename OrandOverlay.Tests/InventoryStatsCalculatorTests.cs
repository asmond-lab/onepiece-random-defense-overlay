using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class InventoryStatsCalculatorTests
{
    [Fact]
    public void GrowthEligibleSpecialUnitsContributeMapSupportStats()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var stats = new InventoryStatsCalculator(catalog).Calculate(
        [
            Entry("rawcode:510h"),
            Entry("rawcode:F10h"),
            Entry("rawcode:610h"),
            Entry("rawcode:I10h")
        ]);

        Assert.Equal(0.25, stats.Stun, precision: 3);
        Assert.Equal(5, stats.Slow);
        Assert.Equal(3, stats.ArmorReduction);
        Assert.Equal(30, stats.AttackSpeed);
    }

    private static InventoryEntry Entry(string unitId) =>
        new() { UnitId = unitId, Count = 1 };
}
