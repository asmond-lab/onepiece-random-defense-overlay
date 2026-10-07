using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class FirstLegendRewardOutputTests
{
    private readonly DataCatalog _catalog = new();
    public FirstLegendRewardOutputTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData("e016", "luffy_common", false)]
    [InlineData("e016", "rawcode:N00h", false)]
    [InlineData("e016", "rawcode:C10h", true)]
    [InlineData("e017", "luffy_common", false)]
    [InlineData("e017", "rawcode:C10h", false)]
    [InlineData("e017", "rawcode:N00h", true)]
    [InlineData("e019", "rawcode:C10h", false)]
    [InlineData("e019", "rawcode:E20h", true)]
    [InlineData("e018", "rawcode:N00h", false)]
    [InlineData("e018", "luffy_common", true)]
    [InlineData("e0IX", "rawcode:C10h", false)]
    [InlineData("e01A", "rawcode:C10h", false)]
    public void ConsumedWispRequiresItsKnownOutputPool(string wisp, string output, bool matches)
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps((wisp, 1));
        Observe(gate, initial);

        var after = Observe(gate, initial with { RecognitionRevision = 2,
            RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = initial.Inventory.SetItem(output, initial.Inventory.GetValueOrDefault(output) + 1) });

        Assert.Equal(!matches && wisp is "e016" or "e017" or "e018" or "e019", after.AwaitingRewardHand);
        Assert.Equal(matches && wisp is "e016" or "e017", after.FirstLegendRewardHandObserved);
    }

    [Fact]
    public void UnsupportedConsumptionDoesNotEraseKnownPendingAmounts()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e016", 2), ("e0IX", 1), ("e01A", 1));
        Observe(gate, initial);
        var partial = initial with { RecognitionRevision = 2, RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = initial.Inventory.Add("rawcode:C10h", 1) };
        Assert.True(Observe(gate, partial).AwaitingRewardHand);

        var complete = Observe(gate, partial with { RecognitionRevision = 3,
            Inventory = partial.Inventory.SetItem("rawcode:C10h", 2) });

        Assert.False(complete.AwaitingRewardHand);
        Assert.True(complete.FirstLegendRewardHandObserved);
    }

    [Fact]
    public void PendingAmountsCannotBeSatisfiedByTheWrongMixOfTiers()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e016", 2), ("e017", 1));
        Observe(gate, initial);
        var partial = initial with { RecognitionRevision = 2, RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = initial.Inventory.Add("rawcode:C10h", 1).Add("rawcode:N00h", 2) };
        Assert.True(Observe(gate, partial).AwaitingRewardHand);
        Assert.True(Observe(gate, partial with { RecognitionRevision = 3 }).AwaitingRewardHand);

        var complete = Observe(gate, partial with { RecognitionRevision = 4,
            Inventory = partial.Inventory.SetItem("rawcode:C10h", 2) });

        Assert.False(complete.AwaitingRewardHand);
        Assert.True(complete.FirstLegendRewardHandObserved);
    }

    [Fact]
    public void LaterConsumptionCannotReusePreviouslyMatchedOutput()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e016", 2));
        Observe(gate, initial);
        var first = initial.WithWisps(("e016", 1)) with { RecognitionRevision = 2,
            Inventory = initial.Inventory.Add("rawcode:C10h", 1) };
        Assert.False(Observe(gate, first).AwaitingRewardHand);
        var second = first with { RecognitionRevision = 3, RewardWisps = ImmutableDictionary<string, int>.Empty };
        Assert.True(Observe(gate, second).AwaitingRewardHand);
        Assert.True(Observe(gate, second with { RecognitionRevision = 4 }).AwaitingRewardHand);

        Assert.False(Observe(gate, second with { RecognitionRevision = 5,
            Inventory = second.Inventory.SetItem("rawcode:C10h", 2) }).AwaitingRewardHand);
    }

    [Fact]
    public void NewConsumptionAccumulatesWithPreviouslyUnmatchedAmounts()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e016", 2));
        Observe(gate, initial);
        var first = initial.WithWisps(("e016", 1)) with { RecognitionRevision = 2 };
        Assert.True(Observe(gate, first).AwaitingRewardHand);
        var second = first with { RecognitionRevision = 3, RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = first.Inventory.Add("rawcode:C10h", 1) };
        Assert.True(Observe(gate, second).AwaitingRewardHand);
        Assert.False(Observe(gate, second with { RecognitionRevision = 4 }).FirstLegendRewardHandObserved);

        var complete = Observe(gate, second with { RecognitionRevision = 5,
            Inventory = second.Inventory.SetItem("rawcode:C10h", 2) });

        Assert.False(complete.AwaitingRewardHand);
        Assert.True(complete.FirstLegendRewardHandObserved);
    }

    [Fact]
    public void CommonSelectionCannotStandInForCheckpointSpecialReward()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e016", 1), ("e018", 1));
        Observe(gate, initial);

        var commonOnly = Observe(gate, initial.WithWisps(("e016", 1)) with { RecognitionRevision = 2,
            Inventory = initial.Inventory.SetItem("luffy_common", 2) });

        Assert.False(commonOnly.FirstLegendRewardHandObserved);
    }

    [Fact]
    public void MatchingGainBeforeConsumptionIsNotBankedForAFutureWisp()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e016", 1));
        Observe(gate, initial);
        var beforeSpend = initial with { RecognitionRevision = 2, Inventory = initial.Inventory.Add("rawcode:C10h", 1) };
        Observe(gate, beforeSpend);

        var spent = Observe(gate, beforeSpend with { RecognitionRevision = 3, RewardWisps = ImmutableDictionary<string, int>.Empty });

        Assert.True(spent.AwaitingRewardHand);
        Assert.False(spent.FirstLegendRewardHandObserved);
    }

    [Fact]
    public void PendingCraftKeepsEarlierSelectedOutputCreditUntilWispConsumption()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var initial = Frame().WithWisps(("e018", 1));
        initial = initial with { GuidePlan = Observe(gate, initial) };
        gate.Record(initial, new CoachDecision(CoachActionKind.Reward, "selection", "", "", "", "", "")
        {
            RewardWispId = "e018", SelectionBatch = new("rawcode:B30h", [new("luffy_common", 1)])
        });
        var early = initial with { RecognitionRevision = 2, Inventory = initial.Inventory.SetItem("luffy_common", 2) };
        early = early with { GuidePlan = Observe(gate, early) };
        gate.Record(early, new CoachDecision(CoachActionKind.Waiting, "craft-recognition:rawcode:N00h", "", "", "", "", "")
        {
            TargetUnitId = "rawcode:N00h", CraftProgress = new("rawcode:N00h", 1, 0, true)
        });
        var settled = Observe(gate, early with { RecognitionRevision = 3, RewardWisps = ImmutableDictionary<string, int>.Empty });
        Assert.False(settled.AwaitingRewardHand);
        Assert.Equal(0, settled.PendingSelectionOutputs);
    }

    private static CoachFrame Frame() => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1, RecognitionRevision = 1,
        Round = 9, CompletedStoryStage = 4, IsCurrent = true,
        Inventory = ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1)
    };

    private static BulletGuidePlan Observe(FirstLegendRewardGate gate, CoachFrame frame) =>
        gate.Prepare(new(BulletGuideStage.FirstLegend, "rawcode:B30h", false), frame);
}

internal static class RewardOutputTestFrames
{
    internal static CoachFrame WithWisps(this CoachFrame frame, params (string Id, int Count)[] wisps) =>
        frame with { RewardWisps = wisps.ToImmutableDictionary(pair => pair.Id, pair => pair.Count) };
}
