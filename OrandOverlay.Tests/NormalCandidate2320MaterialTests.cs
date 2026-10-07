using Xunit;
using Xunit.Abstractions;

namespace OrandOverlay.Tests;

public sealed class NormalCandidate2320MaterialTests(ITestOutputHelper output)
{
    private static DataCatalog LoadCatalog()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.320");
        return catalog;
    }

    [Theory]
    [InlineData("rawcode:T30h", "unknown")]
    [InlineData("rawcode:I50h", "unknown")]
    [InlineData("rawcode:T30h", "magical")]
    [InlineData("rawcode:I50h", "magical")]
    public void RebeccaConsumption321RetainsIndependentCandidatesWithoutWithinRoleBearDuplicates(
        string rebeccaId, string direction)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.321");
        const string kuma = "rawcode:S40h";
        const string bear = "rawcode:1A0h";
        // An unrelated catalog-listed immortal holds the support stage across consumption.
        var anchor = catalog.AllUnits.Where(unit => NormalCandidateBrowser.Tier(unit) == "불멸")
            .OrderBy(unit => unit.Id, StringComparer.Ordinal).First();
        Assert.Contains(catalog.AllUnits, unit => unit.Id == rebeccaId);
        Assert.Contains(catalog.AllUnits, unit => unit.Id == kuma);
        Assert.Single(catalog.AllUnits, unit => unit.Id == bear);
        var inventory = new Dictionary<string, int>
        {
            [anchor.Id] = 1, [kuma] = 1, [rebeccaId] = 1
        };
        var browser = NormalCandidateBrowser.Create(catalog);
        browser.Update(inventory, true, 1);
        browser.SetDirection(direction);
        // The reported selected target is unknown: do not manufacture a selection.
        var before = browser.Snapshot;
        var beforeSelection = browser.SelectedUnitId;
        var beforeAnchor = browser.FirstUpperId;
        Record("before", before);
        var afterInventory = new Dictionary<string, int>(inventory);
        Assert.True(afterInventory.Remove(rebeccaId));
        browser.Update(afterInventory, true, 1);
        var after = browser.Snapshot;
        Record("after", after);

        Assert.Equal(NormalCandidateStage.Utility, before.Stage);
        Assert.Equal(before.Stage, after.Stage);
        Assert.Equal(before.Direction, after.Direction);
        Assert.Equal(beforeAnchor, browser.FirstUpperId);
        Assert.Null(beforeSelection);
        Assert.Null(browser.SelectedUnitId);
        Assert.True(after.IsCurrent);
        Assert.Equal(1, inventory[rebeccaId]);
        Assert.False(browser.CurrentInventory.ContainsKey(rebeccaId));
        Assert.Equal(1, browser.CurrentInventory[kuma]);
        Assert.Equal(1, browser.CurrentInventory[anchor.Id]);
        Assert.Contains(before.Groups.SelectMany(group => group.Candidates),
            candidate => candidate.Unit.Id != bear && candidate.Unit.Id != rebeccaId);
        foreach (var group in before.Groups)
        {
            var retained = Assert.Single(after.Groups, next => next.Name == group.Name);
            // Removing Rebecca can reveal Rebecca herself; no other membership should change.
            Assert.Equal(IndependentIds(group), IndependentIds(retained));
            Assert.Equal(group.Candidates.Count(candidate => candidate.Unit.Id == bear),
                retained.Candidates.Count(candidate => candidate.Unit.Id == bear));
        }
        foreach (var snapshot in new[] { before, after })
        foreach (var group in snapshot.Groups)
            Assert.Equal(group.Candidates.Count,
                group.Candidates.Select(candidate => candidate.Unit.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        if (direction == "magical")
            Assert.Contains(after.Groups.SelectMany(group => group.Candidates), candidate => candidate.Unit.Id == bear);

        string[] IndependentIds(NormalCandidateGroup group) => group.Candidates
            .Select(candidate => candidate.Unit.Id).Where(id => id != rebeccaId)
            .Order(StringComparer.Ordinal).ToArray();
        void Record(string phase, NormalCandidateSnapshot snapshot)
        {
            var candidates = snapshot.Groups.SelectMany(group => group.Candidates).ToArray();
            output.WriteLine($"{phase}: Rebecca={rebeccaId}; anchor={anchor.Id}; stage={snapshot.Stage}; " +
                $"direction={snapshot.Direction}; selection={browser.SelectedUnitId ?? "<none>"}; " +
                $"groups={snapshot.Groups.Count}; entries={candidates.Length}; " +
                $"distinct={candidates.Select(candidate => candidate.Unit.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()}; " +
                $"BearEntries={candidates.Count(candidate => candidate.Unit.Id == bear)}");
            foreach (var group in snapshot.Groups)
                output.WriteLine($"{phase}/{group.Name}: {string.Join(",", group.Candidates.Select(candidate => candidate.Unit.Id))}");
        }
    }

    [Fact]
    public void Fresh2320TranscendenceCandidateCountsTheRequiredKumaCard()
    {
        var browser = new NormalCandidateBrowser(LoadCatalog().AllUnits);
        browser.Update(new Dictionary<string, int>(), true, 1);
        browser.Select("rawcode:V80H");
        var allocation = browser.SelectedCandidate!.Allocation;
        Assert.NotNull(allocation);
        var kuma = Assert.Single(allocation.Progress.Leaves, leaf => leaf.UnitId == "rawcode:S40h");
        Assert.Equal(1, kuma.RequiredCount);
        Assert.Equal(0, kuma.OwnedCount);
    }

    [Theory]
    [InlineData("S40h", "rawcode:E90H")]
    [InlineData("H00h", "rawcode:V80H")]
    [InlineData("060h", "rawcode:U80H")]
    [InlineData("Y50h", "yamato_transcendent")]
    [InlineData("X50h", "rawcode:5B0H")]
    public void SourceVerifiedMaterialRemainsARequiredObservedCard(string rawcode, string target)
    {
        var catalog = LoadCatalog();
        var materialId = "rawcode:" + rawcode;
        var material = catalog.Unit(materialId);
        Assert.Equal("기타", material.Tier);
        Assert.Contains(rawcode, material.Rawcodes);
        Assert.Empty(material.Recipe);
        var registry = catalog.OfflineBundle!.Recipes;
        var sourceId = Map2320RecipeRegistry.ToAppRawcode(rawcode);
        Assert.Contains(registry.Document.Recipes, recipe => recipe.Conditions.Any(term => term.Kind == "UNIT" && term.Id == sourceId));
        var projection = registry.Project(rawcode);
        if (rawcode is "X50h" or "060h")
        {
            Assert.NotNull(projection);
            Assert.Equal(rawcode == "X50h" ? "UU01" : "YY26", projection.RecipeId);
            Assert.Empty(projection.IngredientsByAppRawcode);
            Assert.Empty(projection.ConditionalRequirements);
            Assert.Equal(rawcode == "X50h" ? 4 : 6, projection.UiMetadata.Length);
            Assert.All(projection.UiMetadata, term => Assert.Equal("UPUN", term.Kind));
        }
        else Assert.Null(projection);

        var browser = new NormalCandidateBrowser(catalog.AllUnits);
        browser.Update(new Dictionary<string, int>(), true, 1);
        browser.Select(target);
        var before = browser.SelectedCandidate!.Allocation;
        Assert.NotNull(before);
        var required = Assert.Single(before.Progress.Leaves, leaf => leaf.UnitId == materialId);
        Assert.True(required.RequiredCount > 0);
        Assert.Equal(0, required.OwnedCount);
        Assert.False(before.ResourceRequirements.ContainsKey(rawcode));
        browser.Update(new Dictionary<string, int> { [materialId] = 1 }, true, 1);
        var after = browser.SelectedCandidate!.Allocation!;
        Assert.Equal(before.Progress.RequiredLeafCount, after.Progress.RequiredLeafCount);
        Assert.Equal(1, after.Progress.OwnedLeafCount);
        Assert.Equal(1, after.ConsumedByUnitId[materialId]);
        browser.InvalidateObservation();
        Assert.Null(browser.SelectedCandidate!.Completion);
    }

    [Fact]
    public void OnlyExplicitAlternativeRecipesRemainUnresolvedInBundledUpperPool()
    {
        var catalog = LoadCatalog();
        var browser = new NormalCandidateBrowser(catalog.AllUnits);
        browser.Update(new Dictionary<string, int>(), true, 1);
        var upperUnits = catalog.AllUnits.Where(NormalCandidateBrowser.IsUpper).ToArray();
        var unknown = new List<string>();
        foreach (var unit in upperUnits)
        {
            browser.Select(unit.Id);
            var candidate = browser.SelectedCandidate!;
            if (candidate.Completion is not null) continue;
            unknown.Add(unit.Id);
            Assert.Null(candidate.Allocation);
        }
        var expected = new[] { "rawcode:KB0H", "rawcode:KB0H_", "rawcode:AA0H", "rawcode:MA0H", "rawcode:BA0H", "rawcode:EA0H" };
        Assert.Equal(expected.Order(StringComparer.Ordinal), unknown.Order(StringComparer.Ordinal));
        output.WriteLine($"Bundled upper candidates: {upperUnits.Length}; calculated: {upperUnits.Length - unknown.Count}; alternative materials: {unknown.Count}");
    }

    [Theory]
    [InlineData("rawcode:unknown", "UNKNOWN", "기타")]
    [InlineData("rawcode:S40h", "S40H", "기타")]
    [InlineData("rawcode:unverified", "S40h", "기타")]
    [InlineData("rawcode:S40h", "S40h", "희귀함")]
    public void ArbitraryMiscellaneousAndMismatchedIdentitiesStayUnverified(string id, string rawcode, string tier)
    {
        var browser = new NormalCandidateBrowser([
            new UnitDefinition { Id = id, Name = id, Tier = tier, Rawcodes = [rawcode] },
            new UnitDefinition { Id = "target", Name = "target", Tier = "희귀함", Recipe = new() { [id] = 1 } }
        ]);
        browser.Update(new Dictionary<string, int> { [id] = 1 }, true, 1);
        browser.Select("target");
        Assert.Null(browser.SelectedCandidate!.Completion);
        Assert.Null(browser.SelectedCandidate.Allocation);
    }
}
