using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideExchangeTests
{
    [Fact]
    public void ProductionPlannerKeepsRewardAndRecognitionAheadOfExchange()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var planner = new BeginnerCoachPlanner(catalog);
        var frame = Frame();
        Assert.Equal("guide1:exchange-trait-wisp", planner.Decide(frame).Id);
        Assert.Equal(CoachActionKind.Reward, planner.Decide(frame with
            { RewardWisps = frame.RewardWisps.Add("e019", 1) }).Kind);
        Assert.Equal(CoachActionKind.Recognition, planner.Decide(frame with { IsCurrent = false }).Kind);
    }

    [Fact]
    public void ExchangeRequiresActualTraitManaAndCooldownObservations()
    {
        var frame = Frame();
        Assert.Equal("guide1:exchange-trait-wisp", BulletGuideExchangePolicy.Decide(frame)?.Id);
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { HelperState = null }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { Signals = frame.Signals.SetItem("trait-points", null) }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { Signals = frame.Signals.SetItem("trait-points", 0) }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { HelperState = new(79.9f, 10000, [new("A082", 1, 0)]) }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { HelperState = new(80, 10000, [new("A082", 1, 1)]) }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { HelperState = new(80, 10000, [new("A082", 1, null)]) }));
    }

    [Fact]
    public void MirrorPaymentExistingWispAndLateCombatAreNotSpentAway()
    {
        var frame = Frame();
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { Round = 9 }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { IsCurrent = false }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { Paused = true }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { Outcome = "clear" }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with { RewardWisps = frame.RewardWisps.Add("e018", 1) }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with
        {
            Inventory = frame.Inventory.Add("rawcode:S80h", 1),
            GuidePlan = new(BulletGuideStage.AirFoundation, "rawcode:930h", false) { AirCount = 1 }
        }));
        Assert.Null(BulletGuideExchangePolicy.Decide(frame with
        {
            Inventory = frame.Inventory.Add(BulletGuidePolicy.GoalId, 1),
            GuidePlan = new(BulletGuideStage.Operating, null, true)
        }));
    }

    private static CoachFrame Frame() => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
        Round = 10, CompletedStoryStage = 3, IsCurrent = true, Difficulty = "악몽",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1),
        GuidePlan = new(BulletGuideStage.FirstLegend, "rawcode:HA0h", false),
        Signals = ImmutableDictionary<string, long?>.Empty.Add("trait-points", 1),
        HelperState = new(80, 10000, [new("A082", 1, 0)])
    };
}
