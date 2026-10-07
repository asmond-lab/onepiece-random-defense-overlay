using Xunit;

namespace OrandOverlay.Tests;

public sealed class GrowthMaterialInventoryTests
{
    private static readonly Lazy<DataCatalog> Bundled = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static DataCatalog Catalog => Bundled.Value;
    private static readonly Warcraft300Diagnostic.View View = new(0x100000, 0, 0x110000);
    private static uint Raw(string code) { Assert.True(RawcodeCodec.TryParse(code, out var raw)); return raw; }
    private static string Id(string code) => new RawcodeUnitMap(Catalog).Map(new Dictionary<uint, int> { [Raw(code)] = 1 }).Entries.Single().UnitId;
    private static Warcraft300HandleStamp Stamp(ulong pointer, uint serial = 1) =>
        new(0x70000000, pointer, 0x72792E78, 0x72F807F0, 0x120000, 1, serial,
            0x130000, 128, 0x130010, 0xFFFFFFFE, 0x140000, serial, 0x2B61676C, pointer, 0, 0);
    private static Warcraft300Diagnostic.Unit Unit(string code, byte owner, ulong pointer) => new(pointer, owner, Raw(code), Stamp(pointer));
    private static Warcraft300Diagnostic.Inventory Inventory(params Warcraft300Diagnostic.Unit[] units)
    {
        var owned = units.Where(u => u.Owner == View.Slot).ToArray();
        return new(View, units.Length, owned.Length, units.Length - owned.Length,
            owned.GroupBy(u => u.Rawcode).ToDictionary(g => g.Key, g => g.Count())) { Units = units };
    }
    private static GrowthMaterialInventory.Projection Project(Warcraft300Diagnostic.Inventory inventory, Warcraft300Diagnostic.Unit growth) =>
        GrowthMaterialInventory.Project(inventory, View, growth.Address, growth.Rawcode, growth.Allocation,
            new RawcodeUnitMap(Catalog).IsGrowthUnit);
    private static Dictionary<string, int> Counts(GrowthMaterialInventory.Projection projected) =>
        new RawcodeUnitMap(Catalog).Map(projected.Rawcodes).Entries.ToDictionary(e => e.UnitId, e => e.Count);

    [Fact]
    public void DormantHelmeppoIsTheExistingSpecialAndCreditsKizaruMaterialsImmediately()
    {
        var growth = Unit("I10h", 27, 0x200000);
        var before = Inventory(growth);
        var projection = Project(before, growth);
        var counts = Counts(projection);
        var id = Id("I10h");
        Assert.Equal("특별함", Catalog.Unit(id).Tier);
        Assert.Single(counts); Assert.Equal(1, counts[id]);
        Assert.Single(projection.GrowingRawcodes); Assert.Equal(1, projection.AddedObjects);
        Assert.Empty(before.Rawcodes); // Pure projection, not a cumulative bonus.
        var calculator = new RecipeCompletionCalculator(Catalog.Unit);
        var empty = calculator.CalculateAllocation([Id("R10h")], new Dictionary<string, int>());
        var withGrowth = calculator.CalculateAllocation([Id("R10h")], counts);
        Assert.Equal(0, empty.Progress.OwnedLeafCount);
        Assert.Equal(14, withGrowth.Progress.RequiredLeafCount);
        Assert.Equal(4, withGrowth.Progress.OwnedLeafCount);
        Assert.Equal(4d / 14, withGrowth.Progress.CompletionRatio, 10);
        Assert.Equal(1, withGrowth.ConsumedByUnitId[id]);
    }

    [Fact]
    public void OneMoriaCannotSatisfyBothMoriaBranchesOfOars()
    {
        var growth = Unit("B00h", 27, 0x200000);
        var counts = Counts(Project(Inventory(growth), growth));
        var allocation = new RecipeCompletionCalculator(Catalog.Unit).CalculateAllocation([Id("020h")], counts);
        Assert.Equal(13, allocation.Progress.RequiredLeafCount);
        Assert.Equal(5, allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, allocation.ConsumedByUnitId[Id("B00h")]);
        Assert.NotEmpty(allocation.Progress.MissingLeaves);
    }

    [Fact]
    public void DifferentOwnedAndDormantMoriaAreTwoRealCardsNotOneFamilyMaximum()
    {
        var owned = Unit("B00h", 0, 0x200000);
        var growth = Unit("B00h", 27, 0x210000);
        var projection = Project(Inventory(owned, growth), growth);
        var counts = Counts(projection);
        Assert.Equal(2, counts[Id("B00h")]); Assert.Equal(1, projection.AddedObjects);
        var allocation = new RecipeCompletionCalculator(Catalog.Unit).CalculateAllocation([Id("020h")], counts);
        Assert.Equal(10, allocation.Progress.OwnedLeafCount); Assert.Equal(13, allocation.Progress.RequiredLeafCount);
    }

    [Fact]
    public void OneSpecialIsNotCreditedAgainAcrossTwoRequiredTargets()
    {
        var growth = Unit("I10h", 27, 0x200000);
        var allocation = new RecipeCompletionCalculator(Catalog.Unit).CalculateAllocation(
            [Id("R10h"), Id("R10h")], Counts(Project(Inventory(growth), growth)));
        Assert.Equal(28, allocation.Progress.RequiredLeafCount); Assert.Equal(4, allocation.Progress.OwnedLeafCount);
        Assert.Equal(1, allocation.ConsumedByUnitId[Id("I10h")]);
    }

    [Fact]
    public void SamePhysicalOwnedUnitIsNotAddedAgain()
    {
        var owned = Unit("I10h", 0, 0x200000);
        var projection = Project(Inventory(owned), owned);
        Assert.Equal(1, projection.Rawcodes[owned.Rawcode]);
        Assert.Equal(0, projection.AddedObjects); Assert.Empty(projection.GrowingRawcodes);
    }

    [Fact]
    public void ExplicitAbsencePlusNormalReplacementDoesNotCarryAnOldBonus()
    {
        var prior = Unit("I10h", 27, 0x200000);
        Assert.Equal(1, Project(Inventory(prior), prior).AddedObjects);
        var replacement = Unit("I10h", 0, 0x210000);
        var next = GrowthMaterialInventory.Project(Inventory(replacement), View, null, null, null, _ => true);
        Assert.Equal(1, next.Rawcodes[replacement.Rawcode]); Assert.Equal(0, next.AddedObjects);
        Assert.Empty(next.GrowingRawcodes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("generation")]
    [InlineData("rawcode")]
    [InlineData("view")]
    [InlineData("foreign")]
    [InlineData("unknown")]
    public void UnboundOrChangedGrowthIsRejected(string fault)
    {
        var growth = Unit("I10h", fault == "foreign" ? (byte)7 : (byte)27, 0x200000);
        var inventory = fault switch { "missing" => Inventory(), "duplicate" => Inventory(growth, growth), _ => Inventory(growth) };
        var view = fault == "view" ? View with { Root = 0x220000 } : View;
        var stamp = fault == "generation" ? growth.Allocation with { Serial = 2 } : growth.Allocation;
        var raw = fault == "rawcode" ? Raw("B00h") : growth.Rawcode;
        Assert.Throws<InvalidDataException>(() => GrowthMaterialInventory.Project(inventory, view, growth.Address,
            raw, stamp, code => fault != "unknown"));
    }

    [Fact]
    public void PartialAbsentObservationIsNotAccepted()
    {
        Assert.Throws<InvalidDataException>(() => GrowthMaterialInventory.Project(Inventory(), View,
            null, Raw("I10h"), null, _ => true));
    }

    [Fact]
    public void FullyAcquiredMaterialsCanStillWaitForTheProtectedGrowthCard()
    {
        var catalog = new DataCatalog();
        var units = (Dictionary<string, UnitDefinition>)catalog.UnitsById;
        units["special"] = new() { Id = "special", Name = "성장형 특별함", Tier = "특별함" };
        units["other"] = new() { Id = "other", Name = "다른 재료", Tier = "흔함" };
        units["goal"] = new() { Id = "goal", Name = "목표", Tier = "희귀함",
            Recipe = new() { ["special"] = 1, ["other"] = 1 }, CombineCommands = ["fixture"] };
        var inventory = new[] { new InventoryEntry { UnitId = "special", Count = 1 }, new InventoryEntry { UnitId = "other", Count = 1 } };
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        var progress = calculator.Calculate(["goal"], inventory.ToDictionary(x => x.UnitId, x => x.Count));
        Assert.Equal(1, progress.CompletionRatio);
        var recommendation = new Recommendation
        {
            Route = new() { Id = "craft:goal", GoalUnitId = "goal", Name = "목표" }, RecipeProgress = progress,
            RemainingCraftSteps = [new() { UnitId = "goal", Name = "목표", Tier = "희귀함", RequiredCount = 1,
                Ingredients = [new() { UnitId = "special", Name = "성장형 특별함", RequiredCount = 1 },
                    new() { UnitId = "other", Name = "다른 재료", RequiredCount = 1 }] }]
        };
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var planner = new AutoCombinePlanner(catalog, hotkeys);
        Assert.NotEmpty(planner.Plan([recommendation], inventory));
        Assert.Empty(planner.Plan([recommendation], inventory,
            protectedUnitIds: GrowthMaterialInventory.ProtectForCraft(null, ["special"])));
        Assert.Equal(1, calculator.Calculate(["goal"], inventory.ToDictionary(x => x.UnitId, x => x.Count)).CompletionRatio);
    }

    [Fact]
    public void DormantCardRemainsInCompletionButIsReservedFromImmediateCraftPlanning()
    {
        var id = Id("I10h"); var protectedId = Id("B00h");
        var protectedIds = GrowthMaterialInventory.ProtectForCraft([protectedId, id.ToUpperInvariant()], [id]);
        Assert.Equal(2, protectedIds.Count); Assert.Contains(protectedIds, x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        var growth = Unit("I10h", 27, 0x200000);
        Assert.Equal(1, Counts(Project(Inventory(growth), growth))[id]);
    }
}
