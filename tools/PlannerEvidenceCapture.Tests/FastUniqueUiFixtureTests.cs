using OrandOverlay;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class FastUniqueUiFixtureTests
{
    [Theory]
    [InlineData("--bullet-guide")]
    [InlineData("--bullet-guide-live")]
    [InlineData("--native-unit-probe")]
    public void DedicatedArgumentsNeverReachOtherModes(string mode)
    {
        Assert.Throws<ArgumentException>(() => FastUniqueUiArguments.Validate([mode, "--output", "unused", "--build-sha", "synthetic"]));
    }

    [Fact]
    public void DedicatedArgumentsAcceptOnlyExplicitOutputAndBuildBinding()
    {
        FastUniqueUiArguments.Validate(["--output", "unused", "--build-sha", "synthetic"]);
    }

    [Fact]
    public void RequestPreservesReadyBoundaryInventorySignalsAndSyntheticProvenance()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var cases = FastUniqueUiFixture.Build(catalog);
        foreach (var item in cases)
        {
            var request = FastUniqueUiFixture.Request(item, 3, 17);
            Assert.Equal(RecognitionState.Ready, request.State);
            Assert.Equal(item.Boundary, RecognitionPolicy.ShouldResetBeforeReadyInventory(request));
            Assert.Equal(item.Wisps, request.MapSignals.RewardWisps);
            Assert.Equal(item.Inventory.OrderBy(p => p.Key), request.Entries.ToDictionary(e => e.UnitId, e => e.Count).OrderBy(p => p.Key));
            Assert.Empty(request.NativeUnitPointers);
            Assert.Null(request.VerifiedLocalPlayerSlot);
            Assert.Null(request.SyntheticGorosei);
        }
    }

    [Fact]
    public void FocusedCatalogContainsFiniteRewardTransitionsAndPostDeadlineCases()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var cases = FastUniqueUiFixture.Build(catalog);
        foreach (var reward in new[] { "e016", "e017", "e019" })
            foreach (var phase in new[] { "unreceived", "received", "repeat", "spent", "ready-rare" })
                Assert.Contains(cases, c => c.Name == $"{reward}-{phase}");
        foreach (var name in new[] { "deadline-8", "rare-owned-7", "rare-consumed-7", "new-session-7",
            "selection-1", "selection-3", "selection-after-one", "zombie-nonselectable",
            "actual-unknown", "actual-other", "bullet-zero-to-one", "bullet-owned-no-recraft" })
            Assert.Contains(cases, c => c.Name == name);
        Assert.Equal(cases.Count, cases.Select(c => c.Name).Distinct().Count());
        Assert.InRange(cases.Count, 30, 40);
    }

    [Fact]
    public void FocusedCatalogStartsWithAllEightQuestRoundsAndReadyInputs()
    {
        var catalog = new DataCatalog(); catalog.Load(loadCarryPolicy: false);
        var cases = FastUniqueUiFixture.Build(catalog);
        Assert.NotEmpty(cases);
        Assert.Equal(Enumerable.Range(0, 8), cases.Take(8).Select(c => c.Round));
        foreach (var item in cases.Take(8))
        {
            Assert.False(item.Boundary);
            Assert.Equal(catalog.Unit("rawcode:L50h").Recipe, item.Inventory);
        }
    }
}
