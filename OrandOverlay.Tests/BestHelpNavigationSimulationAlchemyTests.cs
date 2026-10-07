using Xunit;

namespace OrandOverlay.Tests;

public sealed class BestHelpNavigationSimulationAlchemyTests
{
    [Fact]
    public void DeadEndSpecialDismantleReturnsExactMissingUnitChildren()
    {
        var result = Simulation().SimulateAlchemy(Input(), Overrides());

        Assert.Equal(BestHelpSimulationDisposition.Confirmed, result.Disposition);
        var action = Assert.Single(result.Dismantles);
        Assert.Equal("dead", action.UnitRawcode);
        Assert.Equal(2, action.ReturnedUnitChildren["leaf-a"]);
        Assert.Equal(1, action.ReturnedUnitChildren["leaf-b"]);
        Assert.Equal(100, result.ManaSpent);
        Assert.Equal(2, result.RouteLeavesGained["leaf-a"]);
        Assert.Equal(1, result.RouteLeavesGained["leaf-b"]);
    }

    [Fact]
    public void AllSpecialsReservedLeavesZeroLegalAlchemyUses()
    {
        var input = Input() with
        {
            ReservedRouteRoots = new HashSet<string>(["dead"], StringComparer.Ordinal)
        };

        var result = Simulation().SimulateAlchemy(input, Overrides());

        Assert.Equal(BestHelpSimulationDisposition.Confirmed, result.Disposition);
        Assert.Empty(result.Dismantles);
        Assert.Equal(0, result.ManaSpent);
    }

    [Fact]
    public void GrowthReservationCannotBeDismantledEvenWhenItClosesRoute()
    {
        var input = Input() with
        {
            ReservedGrowthUnits = new HashSet<string>(["dead"], StringComparer.Ordinal)
        };

        var result = Simulation().SimulateAlchemy(input, Overrides());

        Assert.Empty(result.Dismantles);
    }

    [Fact]
    public void AlchemyReceivesBestHelpSevenTenthsManaPerSecond()
    {
        var input = Input() with { CurrentMana = 93, HorizonSeconds = 10 };

        var result = Simulation().SimulateAlchemy(input, Overrides());

        Assert.Single(result.Dismantles);
        Assert.Equal(100, result.ManaSpent);
    }

    private static BestHelpAlchemyInput Input() => new()
    {
        CurrentMana = 100,
        Inventory = new Dictionary<string, int>(StringComparer.Ordinal) { ["dead"] = 1 },
        UnitsByRawcode = Units(),
        MissingRouteLeaves = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["leaf-a"] = 2,
            ["leaf-b"] = 1
        }
    };

    private static IReadOnlyDictionary<string, UnitDefinition> Units() =>
        new Dictionary<string, UnitDefinition>(StringComparer.Ordinal)
        {
            ["dead"] = Unit("dead", "special"),
            ["leaf-a"] = Unit("leaf-a", "common"),
            ["leaf-b"] = Unit("leaf-b", "uncommon")
        };

    private static UnitDefinition Unit(string id, string tier) => new()
    {
        Id = id, Name = id, Tier = tier
    };

    private static BestHelpRecipeOverrides Overrides() =>
        BestHelpRecipeOverrides.Parse(["dead=leaf-a:2,leaf-b:1,GOLD:5000"],
            new HashSet<string>(["dead", "leaf-a", "leaf-b"], StringComparer.Ordinal));

    private static BestHelpNavigationSimulation Simulation() => new(
        BestHelpNavigationSimulationTestData.Profile());
}
