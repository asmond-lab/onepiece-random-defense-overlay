using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalFirstLegendPurposeCategoryTests
{
    private static UnitDefinition U(string id, string tier, params string[] ingredients) => new()
    {
        Id = id, Name = id, Tier = tier,
        Recipe = ingredients.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count())
    };

    [Fact]
    public void FirstLegendUsesStablePurposeGroupsWithExactGradeAndMetadataMembership()
    {
        var units = new[]
        {
            U("a", "일반"), U("b", "일반"),
            U("owned-story", "전설", "a", "b"),
            U("story", "전설", "a", "b"), U("both", "히든", "a", "b"),
            U("air", "전설", "a", "b"), U("untagged", "히든", "a", "b"),
            U("upper-tagged", "제한됨", "a", "b"), U("immortal", "불멸", "a", "b"),
            U("eternal", "영원", "a", "b"), U("transcendent", "초월", "a", "b"),
            U(NormalSpecialObtainPolicy.TranscendenceKuma, "기타")
        };
        var guide = new[]
        {
            new NormalGuideUnit("owned-story", true, "physical", []),
            new NormalGuideUnit("story", true, "physical", []),
            new NormalGuideUnit("both", true, "both", ["공중이동", "공중이동"]),
            new NormalGuideUnit("air", false, "magical", ["공중이동"]),
            new NormalGuideUnit("upper-tagged", true, "physical", ["공중이동"])
        };
        var browser = new NormalCandidateBrowser(units, guideProfile: guide);

        browser.Update(new Dictionary<string, int>
        {
            ["a"] = 1,
            ["owned-story"] = 1,
            [NormalSpecialObtainPolicy.TranscendenceKuma] = 1
        }, true, 1);
        browser.SetStage(NormalCandidateStage.Legend);

        Assert.Equal(new[] { "스토리", "공중이동", "가까운 조합" },
            browser.Snapshot.Groups.Select(group => group.Name));
        Assert.Equal(new[] { "both", "story" }, Ids(browser, "스토리").Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "air", "both" }, Ids(browser, "공중이동").Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "air", "both", "eternal", "immortal", "story", "transcendent", "untagged", "upper-tagged" },
            Ids(browser, "가까운 조합").Order(StringComparer.Ordinal));
        Assert.DoesNotContain("upper-tagged", Ids(browser, "스토리"));
        Assert.DoesNotContain("upper-tagged", Ids(browser, "공중이동"));
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(group => group.Candidates),
            candidate => candidate.Unit.Id == "owned-story");
        Assert.All(browser.Snapshot.Groups, group =>
            Assert.Equal(group.Candidates.Count, group.Candidates.Select(candidate => candidate.Unit.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        Assert.All(browser.Snapshot.Groups.SelectMany(group => group.Candidates),
            candidate => Assert.False(candidate.UsefulSupport));
    }

    [Fact]
    public void EmptyMetadataGroupsRemainAndClosestRanksAcrossGradesByDistanceThenSteps()
    {
        var browser = new NormalCandidateBrowser([
            U("a", "일반"), U("b", "일반"), U("mid", "희귀함", "a", "b"),
            U("z-legend-more-steps", "전설", "mid"),
            U("a-upper-fewer-steps", "영원", "a", "b"),
            U("m-hidden-tie", "히든", "a", "b", "b")
        ]);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        browser.SetStage(NormalCandidateStage.Legend);

        Assert.Equal(new[] { "스토리", "공중이동", "가까운 조합" },
            browser.Snapshot.Groups.Select(group => group.Name));
        Assert.Empty(browser.Snapshot.Groups[0].Candidates);
        Assert.Empty(browser.Snapshot.Groups[1].Candidates);
        var closest = browser.Snapshot.Groups[2].Candidates;
        Assert.Equal(new[] { "a-upper-fewer-steps", "z-legend-more-steps", "m-hidden-tie" },
            closest.Select(candidate => candidate.Unit.Id));
        Assert.Equal(new long?[] { 1, 1, 2 }, closest.Select(candidate => candidate.MissingLeafCount));
        Assert.Equal(new long?[] { 1, 2, 1 }, closest.Select(candidate => candidate.RecipeStepCount));
    }

    [Fact]
    public void SpecialObtainAndFreshnessFencesStillApplyToAllPurposeGroups()
    {
        var units = new[]
        {
            U("a", "일반"), U("ordinary", "전설", "a"),
            U(NormalSpecialObtainPolicy.RayleighLegend, "전설", "a"),
            U("transcendent", "초월", "a"),
            U(NormalSpecialObtainPolicy.RareWisp, "자원"),
            U(NormalSpecialObtainPolicy.TranscendenceWisp, "자원")
        };
        var guide = new[]
        {
            new NormalGuideUnit(NormalSpecialObtainPolicy.RayleighLegend, true, "physical", ["공중이동"]),
            new NormalGuideUnit("transcendent", true, "physical", ["공중이동"])
        };
        var browser = new NormalCandidateBrowser(units, guideProfile: guide);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        browser.SetStage(NormalCandidateStage.Legend);
        Assert.Equal(new[] { "ordinary" }, Ids(browser, "가까운 조합"));
        Assert.Empty(Ids(browser, "스토리"));
        Assert.Empty(Ids(browser, "공중이동"));

        browser.Update(new Dictionary<string, int>
        {
            ["a"] = 1,
            [NormalSpecialObtainPolicy.RareWisp] = 1,
            [NormalSpecialObtainPolicy.TranscendenceWisp] = 1
        }, true, 1);
        Assert.Contains(NormalSpecialObtainPolicy.RayleighLegend, Ids(browser, "스토리"));
        Assert.Contains("transcendent", Ids(browser, "가까운 조합"));
        Assert.DoesNotContain("transcendent", Ids(browser, "스토리"));
        Assert.DoesNotContain("transcendent", Ids(browser, "공중이동"));

        var currentNames = browser.Snapshot.Groups.Select(group => group.Name).ToArray();
        browser.InvalidateObservation();
        Assert.False(browser.Snapshot.IsCurrent);
        Assert.Equal(currentNames, browser.Snapshot.Groups.Select(group => group.Name));
        browser.Update(new Dictionary<string, int>(), false, 2);
        Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
        Assert.DoesNotContain(browser.Snapshot.Groups, group => group.Name == "가까운 조합");
    }

    [Fact]
    public void BundledRebeccaMetadataPlacesItOnlyInClosest()
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.320");
        var guide = NormalGuideProfile.LoadBundled(catalog.AllUnits.Select(unit => unit.Id));
        var rebecca = Assert.Single(guide, unit => unit.UnitId == "rawcode:T30h");
        Assert.False(rebecca.StoryFast);
        Assert.DoesNotContain("공중이동", rebecca.MovementRoles);
        var rare = catalog.AllUnits.First(unit => NormalCandidateBrowser.Tier(unit) is "희귀함" or "희귀");
        var browser = new NormalCandidateBrowser(catalog.AllUnits, guideProfile: guide);

        browser.Update(new Dictionary<string, int> { [rare.Id] = 1 }, true, 1);

        Assert.DoesNotContain("rawcode:T30h", Ids(browser, "스토리"));
        Assert.DoesNotContain("rawcode:T30h", Ids(browser, "공중이동"));
        Assert.Contains("rawcode:T30h", Ids(browser, "가까운 조합"));
    }

    [Fact]
    public void KaidoGuideAirCategoryRetainsHistoricalMappingBut2322RequiresDragonForm()
    {
        var units = new[] { U("ingredient", "일반"),
            U("rawcode:M70h", "전설", "ingredient"),
            U("rawcode:DA0h", "전설", "ingredient"),
            U("rawcode:WB0h", "전설", "ingredient") };
        var guide = new[] { new NormalGuideUnit("rawcode:M70h", false, "physical", ["공중이동"]),
            new NormalGuideUnit("rawcode:WB0h", false, "physical", ["공중이동"]) };
        var oldBrowser = new NormalCandidateBrowser(units, guideProfile: guide);
        oldBrowser.Update(new Dictionary<string, int> { ["ingredient"] = 1 }, true, 1);
        oldBrowser.SetStage(NormalCandidateStage.Legend);
        Assert.Contains("rawcode:M70h", Ids(oldBrowser, "공중이동"));

        var current = new NormalCandidateBrowser(units, null, guide, "2.322");
        current.Update(new Dictionary<string, int> { ["ingredient"] = 1 }, true, 1);
        current.SetStage(NormalCandidateStage.Legend);
        Assert.Equal(["rawcode:DA0h"], Ids(current, "공중이동"));
    }

    private static IReadOnlyList<string> Ids(NormalCandidateBrowser browser, string group) =>
        browser.Snapshot.Groups.Single(candidateGroup => candidateGroup.Name == group)
            .Candidates.Select(candidate => candidate.Unit.Id).ToArray();
}
