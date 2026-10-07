using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideRayleighTests
{
    [Fact]
    public void CurrentTranscendenceWispNotHistoricalRewardAuthorizesRayleighSelection()
    {
        var frame = new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
            Round = 25, CompletedStoryStage = 10, IsCurrent = true, Difficulty = "악몽",
            Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1),
            GuidePlan = new(BulletGuideStage.AirFoundation, "rawcode:930h", false) { AirCount = 1 },
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e01A", 1)
        };
        var decision = BulletGuideRayleighPolicy.Decide(frame);
        Assert.NotNull(decision);
        Assert.Equal("e01A", decision.RewardWispId);
        Assert.Equal("rawcode:X50h", decision.TargetUnitId);
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame with { RewardWisps = ImmutableDictionary<string, int>.Empty }));
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame with { CompletedStoryStage = 9 }));
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame with { IsCurrent = false }));
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame with { Paused = true }));
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame with { Inventory = frame.Inventory.Add("rawcode:X50h", 1) }));
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame with { Inventory = frame.Inventory.Add("rawcode:S80h", 1) }));
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var planner = new BeginnerCoachPlanner(catalog);
        var current = frame with { ConfirmedNavigation = BulletGuidePolicy.NavigationId };
        Assert.Equal("e01A", planner.Decide(current).RewardWispId);
        Assert.Equal("e019", planner.Decide(current with
            { RewardWisps = current.RewardWisps.Add("e019", 1) }).RewardWispId);
    }
}
