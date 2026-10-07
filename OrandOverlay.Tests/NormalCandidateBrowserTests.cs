using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalCandidateBrowserTests
{
    private static UnitDefinition Unit(string id, string tier, params string[] material) => new()
    {
        Id = id, Name = id, Tier = tier,
        Recipe = material.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count())
    };
    private static UnitDefinition[] Units() =>
    [
        Unit("a", "일반"), Unit("b", "일반"), Unit("rare-a", "희귀함", "a", "b"),
        Unit("rare-b", "희귀함", "a", "b"), Unit("legend", "전설", "rare-a"),
        Unit("upper", "초월", "legend"), Unit("eternal", "영원", "legend"),
        Unit("limited", "제한됨", "legend"), Unit("immortal", "불멸", "legend"),
        Unit("hidden", "히든", "legend"), Unit("mystic", "신비함", "legend"),
        Unit("seraphim", "세라핌", "legend"), Unit("distorted", "왜곡", "legend")
    ];
    [Fact]
    public void StagesAdvanceOnlyOnCurrentObservationsAndNeverRegressOnConsumption()
    {
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(new Dictionary<string, int> { ["rare-a"] = 1 }, false, 1);
        Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
        browser.Update(new Dictionary<string, int> { ["rare-a"] = 1 }, true, 1);
        Assert.Equal(NormalCandidateStage.Legend, browser.Stage);
        browser.Update(new Dictionary<string, int>(), true, 1);
        Assert.Equal(NormalCandidateStage.Legend, browser.Stage);
        browser.Update(new Dictionary<string, int> { ["legend"] = 1 }, true, 1);
        Assert.Equal(NormalCandidateStage.Upper, browser.Stage);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, false, 1);
        Assert.Equal(NormalCandidateStage.Upper, browser.Stage);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, true, 1);
        Assert.Equal("upper", browser.FirstUpperId);
        browser.Update(new Dictionary<string, int> { ["eternal"] = 1 }, true, 1);
        Assert.Equal("upper", browser.FirstUpperId);
        browser.Update(new Dictionary<string, int>(), false, 2);
        Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
        Assert.Null(browser.FirstUpperId);
    }
    [Fact]
    public void UpperPoolContainsOnlyTheFourApprovedTiers()
    {
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(new Dictionary<string, int> { ["legend"] = 1 }, true, 1);
        var ids = browser.Snapshot.Groups.SelectMany(g => g.Candidates).Select(c => c.Unit.Id).Order().ToArray();
        Assert.Equal(new[] { "eternal", "immortal", "limited", "upper" }, ids);
    }
    [Fact]
    public void Ordinary2322YujiroIsSelectableFromUpperAndLegendNearbyWithoutChangingItsGrade()
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.322");
        var yujiro = catalog.Unit("rawcode:2C0h");
        Assert.Equal("신비", yujiro.Tier);
        Assert.Contains("2C0h", yujiro.Rawcodes);
        Assert.NotEmpty(yujiro.Recipe);
        var browser = NormalCandidateBrowser.Create(catalog);
        Assert.False(browser.IsDiagnosticReference);
        browser.Update(new Dictionary<string, int>(), true, 1);
        foreach (var (stage, category) in new[]
        {
            (NormalCandidateStage.Upper, "상위"), (NormalCandidateStage.Legend, "가까운 조합")
        })
        {
            browser.SetStage(stage);
            var group = Assert.Single(browser.Snapshot.Groups, g => g.Name == category);
            var candidate = Assert.Single(group.Candidates, c => c.Unit.Id == "rawcode:2C0h");
            Assert.Equal("신비", candidate.Unit.Tier);
            Assert.False(candidate.Owned);
            browser.Select(candidate.Unit.Id); // ID comes from a materialized group, not the catalog.
            Assert.Equal(candidate.Unit.Id, browser.SelectedUnitId);
            browser.ClearSelection();
        }
    }
    [Theory]
    [InlineData("2.314")]
    [InlineData("2.320")]
    [InlineData("2.321")]
    [InlineData("2.322")]
    public void YujiroExceptionDoesNotAdmitHistoricalOrUnrelatedMysteryCandidates(string version)
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: version);
        var browser = new NormalCandidateBrowser([.. catalog.AllUnits,
            Unit("rawcode:2C0h", "신비"), Unit("other-mystery", "신비"), Unit("other-mystical", "신비함")],
            null, null, version);
        browser.Update(new Dictionary<string, int>(), true, 1);
        foreach (var stage in new[] { NormalCandidateStage.Rare, NormalCandidateStage.Legend,
            NormalCandidateStage.Upper, NormalCandidateStage.Utility })
        {
            browser.SetStage(stage);
            var ids = browser.Snapshot.Groups.SelectMany(g => g.Candidates).Select(c => c.Unit.Id).ToArray();
            Assert.DoesNotContain("other-mystery", ids);
            Assert.DoesNotContain("other-mystical", ids);
            if (version != "2.322" || stage is NormalCandidateStage.Rare or NormalCandidateStage.Utility)
                Assert.DoesNotContain("rawcode:2C0h", ids);
        }
    }
    [Fact]
    public void EachAlternativeGetsTheSameImmutableInventoryAndStableTieOrder()
    {
        var inventory = new Dictionary<string, int> { ["a"] = 1 };
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(inventory, true, 1);
        var candidates = browser.Snapshot.Groups.Single().Candidates;
        Assert.Equal(new[] { "rare-a", "rare-b" }, candidates.Select(c => c.Unit.Id));
        Assert.All(candidates, c => Assert.Equal(0.5, c.Completion));
        Assert.Equal(1, inventory["a"]);
    }
    [Fact]
    public void SelectionAndFoldsSurviveRankChangesAndUnknownFrames()
    {
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        browser.Select("rare-b"); browser.Fold("희귀함", true);
        browser.Update(new Dictionary<string, int> { ["b"] = 4 }, false, 1);
        Assert.Equal("rare-b", browser.SelectedUnitId);
        Assert.Contains("희귀함", browser.CollapsedCategories);
        Assert.False(browser.Snapshot.IsCurrent);
        Assert.All(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => Assert.NotNull(c.Completion));
        browser.Reset(); Assert.Null(browser.SelectedUnitId); Assert.Empty(browser.CollapsedCategories);
    }
    [Fact]
    public void SelectingTheSameCandidateAgainClearsThePin()
    {
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        browser.Select("rare-b");
        Assert.Equal("rare-b", browser.SelectedUnitId);
        browser.Select("rare-b");
        Assert.Equal("rare-b", browser.SelectedUnitId);
        browser.ClearSelection();
        Assert.Null(browser.SelectedUnitId);
        browser.Select("rare-a");
        Assert.Equal("rare-a", browser.SelectedUnitId);
        browser.Select("rare-b");
        Assert.Equal("rare-b", browser.SelectedUnitId);
        browser.ClearSelection();
        Assert.Null(browser.SelectedUnitId);
    }
    [Fact]
    public void CyclesMissingRecipesAndResourceOnlyTargetsNeverClaimCompletion()
    {
        var browser = new NormalCandidateBrowser(new[]
        {
            Unit("cycle", "희귀함", "cycle"), Unit("missing", "희귀함", "absent"),
            Unit("resource", "희귀함", "GOLD"), Unit("empty", "희귀함")
        });
        browser.Update(new Dictionary<string, int>(), true, 1);
        Assert.All(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => Assert.Null(c.Completion));
    }
    [Fact]
    public void UtilityProfileIncludesCompatibleHiddenLegendsAndPreservesAllRoles()
    {
        var roles = new[]
        {
            new NormalUtilityUnit("upper", "upper", "magical", [new("스턴", "0.3", "발동")]),
            new NormalUtilityUnit("legend", "legend", "both", [new("스턴", "0.4", "발동"), new("이감", "35", "고정")]),
            new NormalUtilityUnit("hidden", "hidden", "magical", [new("스턴", "0.4", "발동")])
        };
        var browser = new NormalCandidateBrowser(Units(), roles);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, true, 1);
        Assert.Equal("magical", browser.Snapshot.Direction);
        Assert.Contains(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "hidden");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "마딜+스턴").Candidates, c => c.Unit.Id == "legend");
        Assert.Equal(2, browser.RolesFor("legend").Count);
    }
    [Fact]
    public void PhysicalUtilityExposesAmbBerserkAndRegenCategories()
    {
        var roles = new[]
        {
            new NormalUtilityUnit("limited", "limited", "physical", [new("방깎", "10", "")]),
            new NormalUtilityUnit("legend", "legend", "physical",
            [
                new("아머브레이크", "true", ""),
                new("보스 잡기", "true", ""),
                new("광폭화", "true", ""),
                new("마나 재생", "2.5", ""),
                new("체력 재생", "2.8", "")
            ]),
            new NormalUtilityUnit("hidden", "hidden", "physical", [new("광폭화", "true", "")])
        };
        var browser = new NormalCandidateBrowser(Units(), roles);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        Assert.Equal("physical", browser.Snapshot.Direction);
        Assert.Equal(new[] { "물딜+암브", "물딜+보스 잡기", "물딜+광폭화 잡기", "물딜+마젠", "물딜+체젠" },
            browser.Snapshot.Groups.Select(g => g.Name).Where(name => name.StartsWith("물딜+", StringComparison.Ordinal) && name is not "물딜+방깎"));
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+암브").Candidates, c => c.Unit.Id == "legend");
        Assert.Equal(new[] { "legend" },
            browser.Snapshot.Groups.Single(g => g.Name == "물딜+보스 잡기").Candidates.Select(c => c.Unit.Id));
        Assert.Equal(new[] { "hidden", "legend" },
            browser.Snapshot.Groups.Single(g => g.Name == "물딜+광폭화 잡기").Candidates.Select(c => c.Unit.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+마젠").Candidates, c => c.Unit.Id == "legend");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+체젠").Candidates, c => c.Unit.Id == "legend");
        Assert.DoesNotContain(browser.Snapshot.Groups.Select(g => g.Name), name => name.StartsWith("마딜+", StringComparison.Ordinal));
    }
    [Fact]
    public void PhysicalFirstUpperManaGroupContainsPhysicalJinbeNotMagicalSugar()
    {
        var browser = new NormalCandidateBrowser(Units(),
        [
            new("limited", "limited", "physical", [new("방깎", "10", "")]),
            new("legend", "jinbe", "physical", [new("마나 재생", "2.5", "")]),
            new("hidden", "sugar", "magical", [new("마나 재생", "1.25", "")])
        ]);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        Assert.Equal("physical", browser.Snapshot.Direction);
        var mana = Assert.Single(browser.Snapshot.Groups, g => g.Name == "물딜+마젠");
        Assert.Equal(new[] { "legend" }, mana.Candidates.Select(c => c.Unit.Id));
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "hidden");
        browser.SetDirection("magical");
        var magicMana = Assert.Single(browser.Snapshot.Groups, g => g.Name == "마딜+마젠");
        Assert.Equal(new[] { "hidden" }, magicMana.Candidates.Select(c => c.Unit.Id));
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "legend");
        browser.Reset();
        browser.Update(new Dictionary<string, int>(), true, 1);
        Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
        Assert.DoesNotContain(browser.Snapshot.Groups.Select(g => g.Name), name => name.Contains('+', StringComparison.Ordinal));
    }
    [Fact]
    public void BearSeraphimSitsInMagicBossBerserkDamageAndShred()
    {
        var browser = new NormalCandidateBrowser([.. Units(), Unit("rawcode:1A0h", "세라핌", "legend")]);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        browser.SetDirection("physical");
        Assert.DoesNotContain(browser.Snapshot.Groups.Where(g => g.Name.StartsWith("물딜+", StringComparison.Ordinal)).SelectMany(g => g.Candidates), c => c.Unit.Id == "rawcode:1A0h");
        browser.SetDirection("magical");
        Assert.Equal("magical", browser.Snapshot.Direction);
        foreach (var name in new[] { "마딜+보스 잡기", "마딜+광폭화 잡기", "마딜+마뎀증", "마딜+마방깎" })
            Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == name).Candidates, c => c.Unit.Id == "rawcode:1A0h");
        Assert.DoesNotContain(browser.Snapshot.Groups.Select(g => g.Name), name => name.StartsWith("물딜+", StringComparison.Ordinal));
    }
    [Fact]
    public void HawkSeraphimSitsInPhysicalShredBossBerserkAndAmb()
    {
        var browser = new NormalCandidateBrowser([.. Units(), Unit("rawcode:3A0h", "세라핌", "legend")],
            [new("limited", "limited", "physical", [new("방깎", "10", "")])]);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        foreach (var name in new[] { "물딜+방깎", "물딜+보스 잡기", "물딜+광폭화 잡기", "물딜+암브" })
            Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == name).Candidates, c => c.Unit.Id == "rawcode:3A0h");
        browser.SetDirection("magical");
        Assert.DoesNotContain(browser.Snapshot.Groups.Where(g => g.Name.StartsWith("마딜+", StringComparison.Ordinal)).SelectMany(g => g.Candidates), c => c.Unit.Id == "rawcode:3A0h");
    }
    [Fact]
    public void SharkAndSnakeSeraphimFollowPhysicalAndMagicalUtilities()
    {
        var browser = new NormalCandidateBrowser(
            [.. Units(), Unit("rawcode:0A0h", "세라핌", "legend"), Unit("rawcode:Y90h", "세라핌", "legend")],
            [new("limited", "limited", "physical", [new("방깎", "10", "")])]);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        foreach (var name in new[] { "물딜+방깎", "물딜+암브", "물딜+공증", "물딜+체젠", "물딜+스턴" })
            Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == name).Candidates, c => c.Unit.Id == "rawcode:0A0h");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "공중이동").Candidates, c => c.Unit.Id == "rawcode:0A0h");
        Assert.DoesNotContain(browser.Snapshot.Groups.Where(g => g.Name.StartsWith("물딜+", StringComparison.Ordinal)).SelectMany(g => g.Candidates), c => c.Unit.Id == "rawcode:Y90h");
        browser.SetDirection("magical");
        foreach (var name in new[] { "마딜+폭뎀증", "마딜+끝딜", "마딜+범위 끝딜", "마딜+스턴" })
            Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == name).Candidates, c => c.Unit.Id == "rawcode:Y90h");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "공중이동").Candidates, c => c.Unit.Id == "rawcode:Y90h");
        Assert.DoesNotContain(browser.Snapshot.Groups.Where(g => g.Name.StartsWith("마딜+", StringComparison.Ordinal)).SelectMany(g => g.Candidates), c => c.Unit.Id == "rawcode:0A0h");
    }
    [Fact]
    public void UtilityClassifiesSpecialRareLegendHiddenPirateSeraphimAndUpper()
    {
        var browser = new NormalCandidateBrowser(
            [.. Units(), Unit("special", "특별함"), Unit("pirate", "해적선"), Unit("rawcode:1A0h", "세라핌", "legend")],
            [
                new("limited", "limited", "physical", [new("방깎", "10", "")]),
                new("special", "special", "physical", [new("공속", "10", "")]),
                new("rare-a", "rare-a", "physical", [new("마나 재생", "0.6", "")]),
                new("legend", "legend", "physical", [new("스턴", "0.5", "")]),
                new("hidden", "hidden", "physical", [new("이감", "20", "")]),
                new("pirate", "pirate", "physical", [new("공증", "15", "")]),
                new("immortal", "immortal", "physical", [new("아머브레이크", "true", "")])
            ]);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+공속").Candidates, c => c.Unit.Id == "special");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+마젠").Candidates, c => c.Unit.Id == "rare-a");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+스턴").Candidates, c => c.Unit.Id == "legend");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+이감").Candidates, c => c.Unit.Id == "hidden");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+공증").Candidates, c => c.Unit.Id == "pirate");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+암브").Candidates, c => c.Unit.Id == "immortal");
        browser.SetDirection("magical");
        foreach (var name in new[] { "마딜+보스 잡기", "마딜+광폭화 잡기", "마딜+마뎀증", "마딜+마방깎" })
            Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == name).Candidates, c => c.Unit.Id == "rawcode:1A0h");
    }
    [Fact]
    public void CatalogAbilitiesPutRareAndSpecialIntoUtilityGroups()
    {
        var browser = new NormalCandidateBrowser(
        [
            .. Units(),
            new() { Id = "catalog-rare", Name = "catalog-rare", Tier = "희귀함",
                OfficialAbilities = [new() { Name = "마나 재생", DisplayValue = "0.6" }] },
            new() { Id = "catalog-special", Name = "catalog-special", Tier = "특별함",
                OfficialAbilities = [new() { Name = "방어력 감소", DisplayValue = "12" }] }
        ], [new("limited", "limited", "physical", [new("방깎", "10", "")])]);
        browser.Update(new Dictionary<string, int> { ["limited"] = 1 }, true, 1);
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+마젠").Candidates, c => c.Unit.Id == "catalog-rare");
        Assert.Contains(browser.Snapshot.Groups.Single(g => g.Name == "물딜+방깎").Candidates, c => c.Unit.Id == "catalog-special");
    }
    [Fact]
    public void MissingProfileOrAmbiguousFirstUpperDoesNotInventDirection()
    {
        var browser = new NormalCandidateBrowser(Units());
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["eternal"] = 1 }, true, 1);
        Assert.Null(browser.FirstUpperId); Assert.Equal("unknown", browser.Snapshot.Direction);
        Assert.Empty(browser.Snapshot.Groups);
        Assert.Empty(NormalCandidateBrowser.LoadProfile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }
    [Fact]
    public void NormalViewConstructsAndRendersWithoutStartingApplicationOrGame()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var browser = new NormalCandidateBrowser(Units());
                browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
                browser.Select("rare-b"); browser.Fold("희귀함", true);
                var type = typeof(NormalCandidateBrowser).Assembly.GetType("OrandOverlay.NormalCandidateView")!;
                var view = Activator.CreateInstance(type)!;
                type.GetMethod("SetModel")!.Invoke(view, [browser]);
                type.GetMethod("Render")!.Invoke(view, null);
                Assert.Equal("rare-b", browser.SelectedUnitId);
                Assert.Contains("희귀함", browser.CollapsedCategories);
            }
            catch (Exception e) { error = e; }
        });
        thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA rendering timed out");
        Assert.Null(error);
    }
}
