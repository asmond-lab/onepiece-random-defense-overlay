using Xunit;

namespace OrandOverlay.Tests;

public sealed class NormalPartnerProgressionTests
{
    private static UnitDefinition U(string id, string tier, params string[] ingredients) => new()
    {
        Id = id, Name = id, Tier = tier,
        Recipe = ingredients.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count())
    };
    private static NormalCandidateBrowser Browser() => new([
        U("a", "일반"), U("upper", "초월", "a"), U("legend", "전설", "a"), U("hidden", "히든", "a"),
        U("other-upper", "영원", "a"), U("rare", "희귀함", "a"), U("seraphim", "세라핌", "a")], guideProfile:
        [new NormalGuideUnit("upper", false, "unknown", [])
            { RecommendedPartners = ["legend", "hidden", "other-upper", "rare", "seraphim", "absent", "upper"] },
        new("legend", false, "unknown", []), new("hidden", false, "magical", []), new("other-upper", false, "physical", [])]);

    [Fact]
    public void ExplicitAuthorPairsWorkWithoutInventingDamageDirection()
    {
        var browser = Browser();
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["a"] = 1 }, true, 1);
        var group = Assert.Single(browser.Snapshot.Groups);
        Assert.Equal("주력 궁합", group.Name);
        Assert.Equal(new[] { "hidden", "legend", "other-upper", "rare", "seraphim" }, group.Candidates.Select(candidate => candidate.Unit.Id));
        Assert.All(group.Candidates, candidate =>
        {
            Assert.True(candidate.UsefulSupport);
            Assert.True(browser.CanHighlight(candidate));
            Assert.Equal("upper 추천 조합", candidate.RecommendationReason);
        });
        Assert.Equal("unknown", browser.FirstUpperDirection);
        Assert.Equal("unknown", browser.Snapshot.Direction);
        Assert.Equal("unknown", group.Candidates.Single(candidate => candidate.Unit.Id == "legend").DamageType);
        browser.SetDirection("physical");
        Assert.Contains(browser.Snapshot.Groups.Single(value => value.Name == "주력 궁합").Candidates,
            candidate => candidate.Unit.Id == "hidden" && browser.CanHighlight(candidate));
    }
    [Fact]
    public void AlreadyObservedPairsDisappearAndExpiredPairsNeverHighlight()
    {
        var browser = Browser();
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["legend"] = 1, ["a"] = 1 }, true, 1);
        Assert.DoesNotContain(browser.Snapshot.Groups.Single(group => group.Name == "주력 궁합").Candidates,
            candidate => candidate.Unit.Id == "legend");
        browser.Select("legend"); Assert.False(browser.CanHighlight(browser.SelectedCandidate!));
        var anchor = browser.FirstUpperId;
        browser.InvalidateObservation();
        Assert.Equal(anchor, browser.FirstUpperId);
        Assert.False(browser.Snapshot.IsCurrent);
        var expired = browser.Snapshot.Groups.Single(group => group.Name == "주력 궁합").Candidates;
        Assert.DoesNotContain(expired, candidate => candidate.Unit.Id == "legend");
        Assert.All(expired, candidate =>
        {
            Assert.True(candidate.UsefulSupport);
            Assert.True(browser.CanHighlight(candidate));
            Assert.Equal("upper 추천 조합", candidate.RecommendationReason);
        });
    }
    [Fact]
    public void AmbiguousUppersNeedAnAnchorBeforeAuthorPairsAreUsed()
    {
        var browser = Browser();
        browser.Update(new Dictionary<string, int> { ["upper"] = 1, ["other-upper"] = 1 }, true, 1);
        Assert.Null(browser.FirstUpperId);
        Assert.DoesNotContain(browser.Snapshot.Groups, group => group.Name == "주력 궁합");
        Assert.True(browser.SelectFirstUpper("upper"));
        Assert.Contains(browser.Snapshot.Groups, group => group.Name == "주력 궁합");
    }
}
