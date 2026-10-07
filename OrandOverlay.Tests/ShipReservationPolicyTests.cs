using Xunit;
namespace OrandOverlay.Tests;

public sealed class ShipReservationPolicyTests
{
    [Fact]
    public void UnownedCatalogTopWithoutRecipeHasUnknownDemand()
    {
        var path = Path.Combine(Path.GetTempPath(), "nasjuro-missing-recipe-" + Guid.NewGuid() + ".json");
        try {
            File.WriteAllText(path, """{"schemaVersion":1,"units":[{"id":"fixture_top_unknown","name":"테스트 미등록 조합","tier":"초월"}]}""");
            var c = new DataCatalog(path); c.Load(loadCarryPolicy:false);
            var f = NasjuroWispAdvicePolicyTests.Frame() with { SelectedGoalIds = ["fixture_top_unknown"] };
            Assert.Empty(c.Unit("fixture_top_unknown").Recipe);
            Assert.Contains(c.AllUnits,u => u.Id == "fixture_top_unknown");
            Assert.False(ShipReservationPolicy.Evaluate(f,c).IsKnown);
        } finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("selected")][InlineData("goal")][InlineData("target")][InlineData("committed")][InlineData("protected")]
    public void EveryRootReservesTwoEternalMihawkPirateShips(string source)
    {
        var f = NasjuroWispAdvicePolicyTests.Frame();
        f = f with { GuidePlan = f.GuidePlan! with { TargetUnitId = null, ProtectedUnitIds = [] },
            Inventory = f.Inventory.Add(ShipReservationPolicy.Pirate,3) };
        f = source switch {
            "selected" => f with { SelectedGoalIds = ["rawcode:850h"] }, "goal" => f with { GoalId = "rawcode:850h" },
            "target" => f with { GuidePlan = f.GuidePlan! with { TargetUnitId = "rawcode:850h" } },
            "committed" => f with { CommittedCraftUnitId = "rawcode:850h" },
            _ => f with { GuidePlan = f.GuidePlan! with { ProtectedUnitIds = ["rawcode:850h"] } } };
        var r = ShipReservationPolicy.Evaluate(f,NasjuroWispAdvicePolicyTests.Catalog());
        Assert.True(r.IsKnown);
        Assert.Equal(new ShipReservation(2,1,0),r.Ships[ShipReservationPolicy.Pirate]);
    }
    [Fact]
    public void ExistingAncientPositiveHasKnownDemand()
    {
        var f = BulletGuideAncientShipTests.Frame();
        var c = NasjuroWispAdvicePolicyTests.Catalog();
        var ids = c.AllUnits.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        void Visit(string id, HashSet<string> seen) {
            var u = c.Unit(id); if (u.Tier == "자원" || !seen.Add(id)) return;
            if (!ids.Contains(u.Id)) unknown.Add(id + ":" + u.Name + ":" + u.Tier);
            foreach (var child in u.Recipe.Keys) Visit(child, seen);
        }
        Visit("rawcode:Q30h", new());
        Assert.True(ShipReservationPolicy.Evaluate(f,c).IsKnown, string.Join(";",unknown));
    }
    [Fact]
    public void UnknownRootBlocksAncientConversionRatherThanBypassingProtection()
    {
        var f = BulletGuideAncientShipTests.Frame() with { SelectedGoalIds = ["rawcode:????"] };
        Assert.Null(BulletGuideAncientShipPolicy.Decide(f, NasjuroWispAdvicePolicyTests.Catalog()));
    }
    [Theory]
    [InlineData("selected")][InlineData("goal")][InlineData("target")][InlineData("committed")][InlineData("protected")]
    public void EveryRootReservesTokiAncientShip(string source)
    {
        var f = NasjuroWispAdvicePolicyTests.Frame();
        f = f with { GuidePlan = f.GuidePlan! with { TargetUnitId = null, ProtectedUnitIds = [] }, Inventory = f.Inventory.Add(ShipReservationPolicy.Ancient, 2) };
        f = source switch {
            "selected" => f with { SelectedGoalIds = ["rawcode:780h"] }, "goal" => f with { GoalId = "rawcode:780h" },
            "target" => f with { GuidePlan = f.GuidePlan! with { TargetUnitId = "rawcode:780h" } },
            "committed" => f with { CommittedCraftUnitId = "rawcode:780h" },
            _ => f with { GuidePlan = f.GuidePlan! with { ProtectedUnitIds = ["rawcode:780h"] } } };
        var r = ShipReservationPolicy.Evaluate(f, NasjuroWispAdvicePolicyTests.Catalog());
        Assert.True(r.IsKnown);
        Assert.Equal(new ShipReservation(1, 1, 0), r.Ships[ShipReservationPolicy.Ancient]);
    }
    [Fact]
    public void ProtectedOwnedIntermediateCannotBeAbsorbedByEarlierSortedRoot()
    {
        var f = NasjuroWispAdvicePolicyTests.Frame();
        f = f with { SelectedGoalIds = ["rawcode:2B0H"],
            GuidePlan = f.GuidePlan! with { TargetUnitId = null, ProtectedUnitIds = ["rawcode:U30h"] },
            Inventory = f.Inventory.Add("rawcode:U30h", 1) };
        var r = ShipReservationPolicy.Evaluate(f, NasjuroWispAdvicePolicyTests.Catalog());
        Assert.Equal(0, r.Allocation.ConsumedByUnitId.GetValueOrDefault("rawcode:U30h"));
        Assert.True(r.Ships[ShipReservationPolicy.Pirate].Missing > 0);
    }
    [Fact]
    public void UnknownRecipeIsUnknownNotZeroShipDemand()
    {
        var f = NasjuroWispAdvicePolicyTests.Frame() with { SelectedGoalIds = ["rawcode:????"] };
        Assert.False(ShipReservationPolicy.Evaluate(f, NasjuroWispAdvicePolicyTests.Catalog()).IsKnown);
    }
    [Fact]
    public void SharedSingleBodyAndDuplicateRootsAreAllocatedOnlyOnce()
    {
        var f = NasjuroWispAdvicePolicyTests.Frame();
        f = f with { SelectedGoalIds = ["rawcode:780h", "rawcode:IA0h"], GoalId = "rawcode:780h",
            CommittedCraftUnitId = "rawcode:780h", GuidePlan = f.GuidePlan! with { TargetUnitId = "rawcode:780h", ProtectedUnitIds = ["rawcode:780h"] },
            Inventory = f.Inventory.Add(ShipReservationPolicy.Ancient, 1) };
        var r = ShipReservationPolicy.Evaluate(f, NasjuroWispAdvicePolicyTests.Catalog());
        Assert.Equal(2, r.Roots.Length);
        Assert.Equal(new ShipReservation(1, 0, 1), r.Ships[ShipReservationPolicy.Ancient]);
    }
    [Fact]
    public void HeldTokiDoesNotChargeHistoricalAncientShipAgain()
    {
        var f = NasjuroWispAdvicePolicyTests.Frame();
        f = f with { SelectedGoalIds = ["rawcode:780h"], GuidePlan = f.GuidePlan! with { TargetUnitId = null, ProtectedUnitIds = [] },
            Inventory = f.Inventory.Add("rawcode:780h", 1).Add(ShipReservationPolicy.Ancient, 1) };
        Assert.Equal(new ShipReservation(0, 1, 0), ShipReservationPolicy.Evaluate(f, NasjuroWispAdvicePolicyTests.Catalog()).Ships[ShipReservationPolicy.Ancient]);
    }
}
