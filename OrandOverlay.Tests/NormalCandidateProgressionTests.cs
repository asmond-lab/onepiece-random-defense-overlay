using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalCandidateProgressionTests
{
    private static readonly Lazy<DataCatalog> Data = new(() => { var c = new DataCatalog(); c.Load(mapVersion: "2.320"); return c; });
    private static DiagnosticInventoryObservation Observation(params string[] ids)
    {
        var now = DateTimeOffset.UtcNow;
        return DiagnosticInventoryObservation.Create(Data.Value, Warcraft300Diagnostic.Version, Warcraft300Diagnostic.Hash,
            new string('A', 64), 1, 0, now.AddMilliseconds(-100), now, TimeSpan.FromMilliseconds(100),
            ids.Select(id => new InventoryEntry { UnitId = id, Count = 1 }), []);
    }
    private static UnitDefinition U(string id, string tier, params string[] ingredients) => new()
    {
        Id = id, Name = id, Tier = tier,
        Recipe = ingredients.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count())
    };

    [Fact]
    public void FreshSealedReferenceAdvancesToTheNextObservedStage()
    {
        var browser = new NormalCandidateBrowser(Data.Value.AllUnits) { ReferencePresentationIsValid = () => true };
        var rare = Data.Value.AllUnits.First(u => NormalCandidateBrowser.Tier(u) is "희귀함" or "희귀");
        var upper = Data.Value.AllUnits.First(NormalCandidateBrowser.IsUpper);
        browser.UpdateReference(Observation(rare.Id), 1);
        Assert.Equal(NormalCandidateStage.Legend, browser.Stage);
        browser.UpdateReference(Observation(upper.Id), 1);
        Assert.Equal(NormalCandidateStage.Utility, browser.Stage);
        Assert.Equal(upper.Id, browser.FirstUpperId);
        Assert.False(Observation(upper.Id).GameplayReady);
    }

    [Fact]
    public void OnlySourceBacked2322YujiroAdvancesUpperProgression()
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.322");
        var browser = NormalCandidateBrowser.Create(catalog);
        browser.Update(new Dictionary<string, int> { ["rawcode:2C0h"] = 1 }, true, 1);
        Assert.Equal(NormalCandidateStage.Utility, browser.ProgressStage);
        Assert.Equal("rawcode:2C0h", browser.FirstUpperId);
        Assert.Equal("신비", catalog.Unit(browser.FirstUpperId!).Tier);

        foreach (var version in new[] { "2.314", "2.320", "2.321", "2.322" })
        {
            var other = new NormalCandidateBrowser([U("rawcode:2C0h", "신비"),
                U("other-mystery", "신비"), U("other-mystical", "신비함")], null, null, version);
            other.Update(new Dictionary<string, int> { ["other-mystery"] = 1, ["other-mystical"] = 1 }, true, 1);
            Assert.Equal(NormalCandidateStage.Rare, other.ProgressStage);
            Assert.Null(other.FirstUpperId);
            if (version == "2.322") continue;
            other.Update(new Dictionary<string, int> { ["rawcode:2C0h"] = 1 }, true, 1);
            Assert.Equal(NormalCandidateStage.Rare, other.ProgressStage);
            Assert.Null(other.FirstUpperId);
        }
    }

    [Fact]
    public void HiddenIsALegendCandidateAndObservingItAdvancesToUpper()
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("rare", "희귀함", "a"),
            U("legend", "전설", "rare"), U("hidden", "히든", "rare"), U("upper", "초월", "legend")]);
        browser.Update(new Dictionary<string, int> { ["rare"] = 1 }, true, 1);
        Assert.Contains(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "hidden");
        browser.Update(new Dictionary<string, int> { ["hidden"] = 1 }, true, 1);
        Assert.Equal(NormalCandidateStage.Upper, browser.Stage);
    }

    [Fact]
    public void AbsoluteMissingCardsBeatHigherPercentageWithMoreMissingCards()
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("b", "일반"),
            U("near", "희귀함", "a", "b"), U("far", "희귀함", "a", "a", "a", "a", "b", "b")]);
        browser.Update(new Dictionary<string, int> { ["a"] = 4 }, true, 1);
        var candidates = browser.Snapshot.Groups.Single().Candidates;
        Assert.True(candidates.Single(c => c.Unit.Id == "far").Completion > candidates.Single(c => c.Unit.Id == "near").Completion);
        Assert.Equal("near", candidates[0].Unit.Id);
    }
    [Theory]
    [InlineData(NormalCandidateStage.Legend, "가까운 조합", "eternal", "hidden", "legend")]
    [InlineData(NormalCandidateStage.Upper, "상위", "eternal", "transcendent", null)]
    public void RelatedGradesShareOnePoolSortedAcrossTierBoundaries(
        NormalCandidateStage stage, string groupName, string nearId, string farId, string? lastId)
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("b", "일반"),
            U("legend", "전설", "a", "a", "a", "a", "b", "b"), U("hidden", "히든", "a", "b"),
            U("transcendent", "초월", "a", "a", "a", "a", "b", "b"), U("eternal", "영원", "a", "b")]);
        browser.Update(new Dictionary<string, int> { ["a"] = 4 }, true, 1);
        browser.SetStage(stage);
        var group = browser.Snapshot.Groups.Single(candidateGroup => candidateGroup.Name == groupName);
        if (stage == NormalCandidateStage.Legend)
            Assert.Equal(new[] { "스토리", "공중이동", "가까운 조합" }, browser.Snapshot.Groups.Select(candidateGroup => candidateGroup.Name));
        else
            Assert.Single(browser.Snapshot.Groups);
        Assert.Equal(lastId is null ? new[] { nearId, farId } : new[] { nearId, farId, lastId },
            group.Candidates.Select(candidate => candidate.Unit.Id));
        Assert.Equal(lastId is null ? new long?[] { 1, 2 } : new long?[] { 1, 1, 2 },
            group.Candidates.Select(candidate => candidate.MissingLeafCount));
        Assert.True(group.Candidates[^1].Completion > group.Candidates[0].Completion);
    }
}
