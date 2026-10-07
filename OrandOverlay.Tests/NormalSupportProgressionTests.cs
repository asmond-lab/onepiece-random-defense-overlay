using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalSupportProgressionTests
{
    private static UnitDefinition U(string id, string tier, params string[] ingredients) => new()
    {
        Id = id, Name = id, Tier = tier,
        Recipe = ingredients.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count())
    };
    private static NormalCandidateBrowser Browser() => new([
        U("a", "일반"), U("b", "일반"), U("rare", "희귀함", "a", "b"),
        U("upper", "초월", "rare"), U("armor", "전설", "rare"), U("stun", "전설", "rare"),
        U("slow", "전설", "rare"), U("air", "전설", "rare"), U("terrain", "전설", "rare"),
        U("blink", "히든", "rare"), U("unknown", "전설", "rare"), U("magic", "전설", "rare")],
        [new("upper", "anchor", "physical", [new("스턴", "0.4", "조건")]),
        new("armor", "armor", "physical", [new("방깎", "25", "조건")]),
        new("stun", "stun", "both", [new("스턴", "0.3", "발동")]),
        new("slow", "slow", "physical", [new("스턴", "0.3", "발동"), new("이감", "35", "조건")]),
        new("unknown", "unknown", "unknown", [new("이감", "99", "조건")]),
        new("magic", "magic", "magical", [new("끝딜", "true", "발동")])],
        [new("upper", false, "physical", []), new("air", false, "both", ["공중이동"]),
        new("terrain", false, "physical", ["지형무시이동"]), new("blink", false, "both", ["순간이동"])]);
    private static NormalCandidate Candidate(NormalCandidateBrowser browser, string id)
    { browser.Select(id); return browser.SelectedCandidate!; }

    [Fact]
    public void SupportUsesDirectionAndMissingRolesEvenWhenAllMaterialsAreComplete()
    {
        var browser = Browser();
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["a"] = 1, ["b"] = 1 }, true, 1);
        var armor = Candidate(browser, "armor"); var stun = Candidate(browser, "stun");
        Assert.Equal(1.0, armor.Completion); Assert.Equal(1.0, stun.Completion);
        Assert.False(browser.CanHighlight(armor)); Assert.False(browser.CanHighlight(stun));
        Assert.Empty(armor.RecommendationReason);
        Assert.Empty(Candidate(browser, "slow").RecommendationReason);
        Assert.Contains(browser.Snapshot.Groups, group => group.Name == "물딜+방깎");
        Assert.Contains(browser.Snapshot.Groups, group => group.Name == "물딜+이감");
        Assert.Equal("0.4", browser.RolesFor("upper").Single().Value);
        Assert.Equal("35", browser.RolesFor("slow").Single(r => r.Category == "이감").Value);
        Assert.False(browser.CanHighlight(Candidate(browser, "unknown")));
        Assert.DoesNotContain(browser.Snapshot.Groups.SelectMany(g => g.Candidates), c => c.Unit.Id == "magic");
        browser.SetDirection("magical");
        Assert.Equal("physical", browser.FirstUpperDirection);
        Assert.False(browser.CanHighlight(Candidate(browser, "magic")));
    }
    [Fact]
    public void DistinctMovementCoverageAvoidsAlreadyObservedDuplicates()
    {
        var browser = Browser();
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["air"] = 1 }, true, 1);
        Assert.True(browser.MovementCoverage.Single(c => c.Role == "공중이동").Covered);
        Assert.False(browser.MovementCoverage.Single(c => c.Role == "지형무시이동").Covered);
        Assert.False(browser.MovementCoverage.Single(c => c.Role == "순간이동").Covered);
        Assert.False(browser.CanHighlight(Candidate(browser, "air")));
        Assert.False(browser.CanHighlight(Candidate(browser, "terrain")));
        Assert.False(browser.CanHighlight(Candidate(browser, "blink")));
        Assert.Empty(Candidate(browser, "terrain").RecommendationReason);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["armor"] = 1, ["terrain"] = 1 }, true, 1);
        Assert.False(browser.CanHighlight(Candidate(browser, "armor")));
        Assert.True(browser.MovementCoverage.Single(c => c.Role == "지형무시이동").Covered);
        Assert.False(browser.MovementCoverage.Single(c => c.Role == "공중이동").Covered);
        browser.InvalidateObservation();
        Assert.False(browser.Snapshot.IsCurrent);
        Assert.False(browser.CanHighlight(Candidate(browser, "blink")));
    }
    [Fact]
    public void DuplicateCategoryCardsShareOneCandidateObjectAndRepeatedReadsAreStable()
    {
        var browser = Browser(); browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, true, 1);
        var cards = browser.Snapshot.Groups.SelectMany(g => g.Candidates).Where(c => c.Unit.Id == "slow").ToArray();
        Assert.Equal(2, cards.Length); Assert.Same(cards[0], cards[1]);
        Assert.Same(cards[0], Candidate(browser, "slow"));
        var snapshot = browser.Snapshot;
        browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, true, 1);
        Assert.Same(snapshot, browser.Snapshot); Assert.Same(cards[0], browser.SelectedCandidate);
    }
    [Fact]
    public void CompleteMaterialsAndCardClicksNeverAdvanceObservedProgress()
    {
        var browser = Browser();
        browser.Update(new Dictionary<string, int> { ["a"] = 2, ["b"] = 2 }, true, 1);
        Assert.Equal(1.0, Candidate(browser, "upper").Completion);
        Assert.Equal(NormalCandidateStage.Rare, browser.ProgressStage);
        browser.SetStage(NormalCandidateStage.Utility);
        Assert.Equal(NormalCandidateStage.Rare, browser.ProgressStage);
        browser.ResumeProgress(); Assert.Equal(NormalCandidateStage.Rare, browser.Stage);
    }
    [Fact]
    public void SharedMaterialsAreAllocatedOnceAndDistanceCannotReuseThem()
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("b", "일반"), U("rare", "희귀함", "a", "b"),
            U("target", "전설", "rare", "rare", "a")]);
        var hand = new Dictionary<string, int> { ["rare"] = 1, ["a"] = 1 };
        browser.Update(hand, true, 1);
        var candidate = Candidate(browser, "target");
        Assert.Equal(5, candidate.Allocation!.Progress.RequiredLeafCount);
        Assert.Equal(3, candidate.Allocation.Progress.OwnedLeafCount);
        Assert.Equal(2, candidate.MissingLeafCount); Assert.Equal(2, candidate.RecipeStepCount);
        Assert.Equal(1, hand["rare"]); Assert.Equal(1, hand["a"]);
    }
    [Fact]
    public void FastStoryMetadataDoesNotOverrideMaterialDistanceOrRecipeSteps()
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("b", "일반"),
            U("a-ordinary", "희귀함", "a", "b"), U("fast", "희귀함", "a", "b"),
            U("far-fast", "희귀함", "a", "b", "b")], guideProfile:
            [new("fast", true, "physical", []), new("far-fast", true, "magical", [])]);
        browser.Update(new Dictionary<string, int> { ["a"] = 1 }, true, 1);
        Assert.Equal(new[] { "a-ordinary", "fast", "far-fast" }, browser.Snapshot.Groups.Single().Candidates.Select(c => c.Unit.Id));
        var fast = Candidate(browser, "fast");
        Assert.True(fast.StoryFast); Assert.Equal("physical", fast.DamageType);
        Assert.Empty(fast.RecommendationReason);
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("both")]
    [InlineData("physical")]
    public void MovementDoesNotRequireMatchingOrKnownDamageDirection(string anchorDamage)
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("upper", "초월", "a"),
            U("flyer", "전설", "a"), U("blink", "히든", "a")], guideProfile:
            [new("upper", false, anchorDamage, []), new("flyer", false, "magical", ["공중이동"]),
            new("blink", false, "unknown", ["순간이동"])]);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1 }, true, 1);
        Assert.Contains(browser.Snapshot.Groups, group => group.Name == "공중이동");
        Assert.Contains(browser.Snapshot.Groups, group => group.Name == "순간이동");
        Assert.False(browser.CanHighlight(Candidate(browser, "flyer")));
        Assert.False(browser.CanHighlight(Candidate(browser, "blink")));
        Assert.Empty(Candidate(browser, "blink").RecommendationReason);
    }
    [Fact]
    public void SharedSourceRolesCountAcrossDamageDirectionsAndMagicIncludesMana()
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("upper", "초월", "a"),
            U("provider", "전설", "a"), U("candidate", "전설", "a"), U("mana", "전설", "a")],
            [new("upper", "upper", "magical", []),
            new("provider", "provider", "physical", [new("스턴", "0.3", "발동"), new("이감", "30", "조건")]),
            new("candidate", "candidate", "both", [new("스턴", "0.4", "발동"), new("이감", "99", "조건")]),
            new("mana", "mana", "magical", [new("마나 재생", "0.6", "조건")])]);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["provider"] = 1, ["a"] = 1 }, true, 1);
        Assert.False(browser.CanHighlight(Candidate(browser, "candidate")));
        Assert.Contains(browser.Snapshot.Groups, group => group.Name == "마딜+마젠");
        Assert.False(browser.CanHighlight(Candidate(browser, "mana")));
        Assert.Empty(Candidate(browser, "mana").RecommendationReason);
        Assert.Equal("0.6", browser.RolesFor("mana").Single().Value);
    }
    [Fact]
    public void ExplicitGuideUnknownOverridesOlderUtilityDirection()
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("upper", "초월", "a"), U("support", "전설", "a")],
            [new("upper", "upper", "physical", []), new("support", "support", "physical", [new("방깎", "25", "조건")])],
            [new("upper", false, "unknown", []), new("support", false, "unknown", [])]);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["a"] = 1 }, true, 1);
        Assert.Equal("unknown", browser.FirstUpperDirection);
        Assert.Equal("unknown", browser.Snapshot.Direction);
        browser.SetDirection("physical");
        var candidate = Candidate(browser, "support");
        Assert.Equal("unknown", candidate.DamageType);
        Assert.True(browser.DirectionUnverified(candidate.Unit.Id));
        Assert.False(browser.CanHighlight(candidate));
    }
    [Theory]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("ture", false)]
    [InlineData("끝딜 삭제", false)]
    [InlineData("-1", false)]
    [InlineData("NaN", false)]
    [InlineData("Infinity", false)]
    [InlineData("1e309", false)]
    [InlineData("true", true)]
    [InlineData("0.3", true)]
    public void OnlyPositiveSourceEvidenceCanProvideCoverage(string sourceValue, bool providesRole)
    {
        var browser = new NormalCandidateBrowser([U("a", "일반"), U("upper", "초월", "a"),
            U("provider", "전설", "a"), U("candidate", "전설", "a"), U("uncertain", "전설", "a")],
            [new("upper", "upper", "magical", []),
            new("provider", "provider", "magical", [new("끝딜", sourceValue, sourceValue.Length == 0 ? "끝딜 삭제" : "원문 능력치")]),
            new("candidate", "candidate", "magical", [new("끝딜", "true", "발동")]),
            new("uncertain", "uncertain", "magical", [new("끝딜", sourceValue, "원문 미확인")])]);
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["provider"] = 1, ["a"] = 1 }, true, 1);
        Assert.False(browser.CanHighlight(Candidate(browser, "candidate")));
        Assert.Equal(sourceValue, browser.RolesFor("provider").Single().Value);
        Assert.Contains(browser.Snapshot.Groups.Single(group => group.Name == "마딜+끝딜").Candidates,
            candidate => candidate.Unit.Id == "uncertain");
        Assert.False(browser.CanHighlight(Candidate(browser, "uncertain")));
    }
}
