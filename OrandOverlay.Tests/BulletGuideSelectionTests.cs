using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletGuideSelectionTests
{
    [Fact]
    public void ActualSelectionWispFillsAMissingCommonAndStopsAfterObservation()
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false);
        var hand = new RecipeCompletionCalculator(catalog.Unit).Calculate(["rawcode:B30h"],
            ImmutableDictionary<string, int>.Empty).Leaves.ToImmutableDictionary(leaf => leaf.UnitId,
                leaf => checked((int)leaf.RequiredCount));
        var frame = new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
            Round = 10, CompletedStoryStage = 3, IsCurrent = true, Difficulty = "악몽",
            Inventory = hand.SetItem("luffy_common", hand["luffy_common"] - 1),
            GuidePlan = new(BulletGuideStage.FirstLegend, "rawcode:B30h", false),
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 1),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100)
        };
        var decision = BulletGuideSelectionPolicy.Decide(frame, catalog);
        Assert.NotNull(decision);
        Assert.Equal("luffy_common", decision.TargetUnitId);
        Assert.Equal("e018", decision.RewardWispId);
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with
            { Inventory = hand }, catalog));
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with
            { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e019", 1) }, catalog));
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { IsCurrent = false }, catalog));
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { Paused = true }, catalog));
        var productionFrame = frame with
        {
            Signals = frame.Signals.Add("trait-points", 1),
            HelperState = new(80, 10000, [new("A082", 1, 0)])
        };
        Assert.Equal("e018", new BeginnerCoachPlanner(catalog).Decide(productionFrame).RewardWispId);
    }
}
