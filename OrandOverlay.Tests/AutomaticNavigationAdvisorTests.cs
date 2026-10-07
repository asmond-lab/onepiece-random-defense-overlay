using Xunit;

namespace OrandOverlay.Tests;

public sealed class AutomaticNavigationAdvisorTests
{
    [Fact]
    public void GoalAndTopCountChangeTheAutomaticRecommendation()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var inventory = new[] { new InventoryEntry { UnitId = "luffy_common" } };
        var garp = catalog.Unit("rawcode:C40h");
        var kizaru = catalog.Unit("rawcode:5B0H");
        var single = AutomaticNavigationAdvisor.Select(catalog, inventory, [garp], 21, 1, null);
        var pair = AutomaticNavigationAdvisor.Select(catalog, inventory, [garp, kizaru], 21, 2, null);
        Assert.Equal("PathOfKings.BountyHunter", single?.OptionId);
        Assert.Equal("AlliedForces.TraitEngineering", pair?.OptionId);
        Assert.True(NavigationProfiles.Find(pair!.OptionId).AllowsMultipleTopUnits);
    }

    [Fact]
    public void IncompatibleScoredWinnerIsNotUsedForTwoGoals()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var scored = new NavigationIntervalScoringResult(NavigationRecommendationState.Actionable,
            NavigationScoringRegime.SecureCore, "PathOfKings.BountyHunter", true, false, [], [], []);
        var advice = AutomaticNavigationAdvisor.Select(catalog,
            [new InventoryEntry { UnitId = "luffy_common" }], [catalog.Unit("rawcode:C40h")], 21, 2, scored);
        Assert.NotNull(advice);
        Assert.True(NavigationProfiles.Find(advice.OptionId).AllowsMultipleTopUnits);
    }

    [Fact]
    public void ClosedWindowKeepsEvaluationButDoesNotClaimSelectionIsActionable()
    {
        var catalog = new DataCatalog();
        var advice = AutomaticNavigationAdvisor.Select(catalog,
            [new InventoryEntry { UnitId = "unit" }], [], 24, 2, null);
        Assert.NotNull(advice);
        Assert.False(advice.CanSelectNow);
    }
}
