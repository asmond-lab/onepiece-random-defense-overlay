using System.Collections.Immutable;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ViviChangedFormTests
{
    [Fact]
    public void ChangedFormPreservesMemoryIdentityAndCatalogRecipe()
    {
        Assert.True(RawcodeCodec.TryParse("W50h", out var changed));
        var catalog = new DataCatalog();
        catalog.Load();
        var changedUnit = catalog.Unit("rawcode:W50h");

        Assert.Equal("rawcode:W50h", RawcodeCodec.DynamicUnitId(changed));
        Assert.Equal("W50h", RawcodeAliases.CanonicalForStats("W50h"));
        Assert.Equal("변화된", changedUnit.Tier);
        Assert.NotEmpty(changedUnit.Recipe);
        Assert.Equal("rawcode:W50h", changedUnit.Id);
    }

    [Fact]
    public void RecognizedTransformationStopsCoachFromRequestingTheSameCraft()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        var map = new RawcodeUnitMap(catalog);
        var engine = new RecommendationEngine(catalog);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(
            AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var planner = new AutoCombinePlanner(catalog, hotkeys);
        var coach = new BeginnerCoachSession(catalog);

        CoachFrame Recognize(string rawcode, long revision)
        {
            Assert.True(RawcodeCodec.TryParse(rawcode, out var code));
            var inventory = map.Map(new Dictionary<uint, int> { [code] = 1 }).Entries;
            var recommendations = engine.RecommendNearestCrafts("rawcode:W50h", inventory, 1);
            return new CoachFrame
            {
                MatchGeneration = 1, Revision = revision, Round = 15,
                CompletedStoryStage = 8, IsCurrent = true, Difficulty = "신",
                Inventory = inventory.ToImmutableDictionary(entry => entry.UnitId, entry => entry.Count),
                Recommendations = recommendations,
                CraftSteps = planner.Plan(recommendations, inventory)
            };
        }

        var before = coach.Update(Recognize("O10h", 1));
        Assert.Equal(CoachActionKind.Economy, before.Kind);
        Assert.Equal("resource:rawcode:W50h", before.Id);

        var transformed = Recognize("W50h", 2);
        var after = coach.Update(transformed);
        Assert.DoesNotContain(transformed.CraftSteps, step => step.TargetUnitId == "rawcode:W50h");
        Assert.NotEqual(before.Id, after.Id);
        Assert.Equal(1, transformed.Inventory.GetValueOrDefault("rawcode:W50h"));
        Assert.Equal(0, transformed.Inventory.GetValueOrDefault("rawcode:O10h"));
    }

    [Fact]
    public void RareAndTransformedViviRemainSeparateCardsWithTransformedSupportStats()
    {
        var catalog = new DataCatalog();
        catalog.Load();
        Assert.True(RawcodeCodec.TryParse("O10h", out var rare));
        Assert.True(RawcodeCodec.TryParse("W50h", out var transformed));
        var mapped = new RawcodeUnitMap(catalog).Map(new Dictionary<uint, int>
        {
            [rare] = 1, [transformed] = 2
        });

        Assert.Equal(0, mapped.UnknownCount);
        Assert.Collection(mapped.Entries.OrderBy(entry => entry.UnitId),
            entry => { Assert.Equal("rawcode:O10h", entry.UnitId); Assert.Equal(1, entry.Count); },
            entry => { Assert.Equal("rawcode:W50h", entry.UnitId); Assert.Equal(2, entry.Count); });
        var stats = new InventoryStatsCalculator(catalog).Calculate(
            mapped.Entries.Where(entry => entry.UnitId == "rawcode:W50h"));
        Assert.Equal(40, stats.TotalSlow);
        Assert.Equal(22, stats.TotalArmorReduction);
    }

    [Fact]
    public void LilithRawcodeIsNotCollapsedIntoLegacyDockingUnit()
    {
        Assert.True(RawcodeCodec.TryParse("BA0H", out var lilith));
        var catalog = new DataCatalog();
        catalog.Load();

        Assert.Equal("rawcode:BA0H", RawcodeCodec.DynamicUnitId(lilith));
        Assert.Equal("릴리스", catalog.Unit("rawcode:BA0H").Name);
    }
}
