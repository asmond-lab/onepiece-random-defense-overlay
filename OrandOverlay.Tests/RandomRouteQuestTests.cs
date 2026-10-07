using Xunit;

namespace OrandOverlay.Tests;

public sealed class RandomRouteQuestTests
{
    [Fact]
    public void UnreadQuestsAreNotAbsentOrCompleted()
    {
        Assert.False(RouteQuestSnapshot.Unknown.IsVerified);
        Assert.All(RouteQuestCatalog.All, quest =>
            Assert.Equal(RouteQuestStatus.Unknown, RouteQuestSnapshot.Unknown.Status(quest.Id)));
    }

    [Fact]
    public void CurrentThreeSlotsDetermineAssignmentsNotFixedQuestKinds()
    {
        var observed = RouteQuestSnapshot.FromVerifiedSlots([
            new(2, "Q011", false), new(0, "Q008", true), new(1, "Q016", false)]);
        Assert.True(observed.IsVerified);
        Assert.Equal(RouteQuestStatus.Completed, observed.Status("Q008"));
        Assert.Equal(RouteQuestStatus.Active, observed.Status("Q016"));
        Assert.Equal(RouteQuestStatus.Inactive, observed.Status("Q010"));
        var nextMatch = RouteQuestSnapshot.FromVerifiedSlots([
            new(0, "Q002", false), new(1, "Q009", false), new(2, "Q015", false)]);
        Assert.Equal(RouteQuestStatus.Inactive, nextMatch.Status("Q008"));
        Assert.Equal(RouteQuestStatus.Active, nextMatch.Status("Q009"));
        Assert.Equal(RouteQuestStatus.Unknown, RouteQuestSnapshot.Unknown.Status("Q009"));
    }

    [Fact]
    public void IncompleteDuplicateOrUnknownSlotsFailClosed()
    {
        Assert.False(RouteQuestSnapshot.FromVerifiedSlots([new(0, "Q008", false)]).IsVerified);
        Assert.False(RouteQuestSnapshot.FromVerifiedSlots([
            new(0, "Q008", false), new(1, "Q008", false), new(2, "Q016", true)]).IsVerified);
        Assert.False(RouteQuestSnapshot.FromVerifiedSlots([
            new(0, "Q008", false), new(1, "Q999", false), new(2, "Q016", true)]).IsVerified);
        Assert.False(RouteQuestSnapshot.FromVerifiedSlots([
            new(0, "Q008", false), new(0, "Q011", false), new(2, "Q016", true)]).IsVerified);
    }

    [Fact]
    public void CatalogCoversEntireOriginalMapPool()
    {
        Assert.Equal(17, RouteQuestCatalog.All.Length);
        Assert.Equal(17, RouteQuestCatalog.All.Select(x => x.Id).Distinct().Count());
        Assert.Equal(2, RouteQuestCatalog.Find("Q008")!.CommonSelectionWisps);
        Assert.Equal(1, RouteQuestCatalog.Find("Q011")!.CommonSelectionWisps);
        Assert.Equal("흔함선택위습 2개", RouteQuestCatalog.Find("Q016")!.Reward);
    }
}
