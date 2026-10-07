using Xunit;

namespace OrandOverlay.Tests;

public sealed class BestHelpNavigationSimulationReverseTests
{
    [Fact]
    public void ReverseAppliesFixedRewardsAndPricesRemovedHighManaActions()
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A0BZ"] = 100,
            ["A0BY"] = 200,
            ["A07T"] = 300,
            ["A07X"] = 400,
            ["A07W"] = 500
        };

        var result = Simulation().SimulateReverse(new()
        {
            RayleighUnitId = "rayleigh-rawcode",
            RayleighRouteGainBp = 900,
            ExcavationStackGainBp = 600,
            HelperActionValuesBp = values,
            RemovedHelperActionIds = new HashSet<string>(["A07W"], StringComparer.Ordinal)
        });

        Assert.Equal(BestHelpSimulationDisposition.Confirmed, result.Disposition);
        Assert.Equal(1, result.RayleighCount);
        Assert.Equal(1, result.ExcavationStacks);
        Assert.True(result.LostHelperActionIds.SequenceEqual(
            ["A07T", "A07W", "A07X", "A0BY", "A0BZ"]));
        Assert.Equal(1_500, result.HelperLossBp);
        Assert.Equal(0, result.NetRouteGainBp);
    }

    [Fact]
    public void ReverseUnknownRemovedActionFailsClosedWithoutNameWhitelist()
    {
        var result = Simulation().SimulateReverse(new()
        {
            RayleighUnitId = "any-source-id",
            RemovedHelperActionIds = new HashSet<string>(["unknown-action"],
                StringComparer.Ordinal)
        });

        Assert.Equal(BestHelpSimulationDisposition.ScenarioGated, result.Disposition);
        Assert.Contains("unknown-action", result.UnknownSpellIds);
    }

    private static BestHelpNavigationSimulation Simulation() => new(
        BestHelpNavigationSimulationTestData.Profile());
}
