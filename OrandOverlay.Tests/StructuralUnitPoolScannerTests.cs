using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StructuralUnitPoolScannerTests
{
    [Fact]
    public void LongRunningProcessDoesNotRaiseVerifiedPoolMinimum()
    {
        var profile = new MemoryProfile { MinimumUnitObjects = 8 };

        var minimum = StructuralUnitPoolScanner.PoolDistinctUnitMinimum(
            observedCUnits: 705, profile);

        Assert.Equal(8, minimum);
        Assert.True(44 >= minimum);
    }
}
