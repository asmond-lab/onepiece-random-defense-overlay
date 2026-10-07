using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RewardLiveReplayTests
{
    private readonly DataCatalog _catalog = new();
    public RewardLiveReplayTests() => _catalog.Load(loadCarryPolicy: false);

    // match-20260909-162045-5dafaf77f7614aa784dc0b59a2ac4b90.jsonl seq95-103.
    // Inventory/stage/round are recorded. e017=2 is reported by seq95-98 decisions;
    // zero at seq99 is reconstructed from the transition away from the owned reward.
    [Fact]
    public void RecordedRoundSevenUncommonOutputsPrecedeWispRemovalAndDoNotCompleteStoryFour()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(95, 7, 2, "e017", 2, new Dictionary<string, int>
        {
            ["rawcode:500h"] = 1, ["rawcode:700h"] = 2, ["rawcode:400h"] = 1,
            ["rawcode:800h"] = 3, ["rawcode:A00h"] = 1, ["rawcode:200h"] = 3,
            ["rawcode:100h"] = 1, ["rawcode:V10h"] = 1, ["rawcode:600h"] = 3,
            ["luffy_common"] = 1, ["rawcode:K10h"] = 1
        });
        Observe(gate, frame, "e017");
        frame = frame with { RecognitionRevision = 96, CompletedStoryStage = 3 };
        Observe(gate, frame, "e017");
        frame = frame with { RecognitionRevision = 97,
            Inventory = frame.Inventory.SetItem("rawcode:A00h", 2).Add("rawcode:N00h", 1) };
        Observe(gate, frame, "e017");
        frame = frame with { RecognitionRevision = 98,
            Inventory = frame.Inventory.SetItem("rawcode:200h", 4).SetItem("rawcode:600h", 4) };
        Observe(gate, frame, "e017");
        frame = frame with { RecognitionRevision = 99, RewardWisps = ImmutableDictionary<string, int>.Empty };
        var plan = Observe(gate, frame);
        Assert.False(plan.AwaitingRewardHand);
        Assert.False(plan.FirstLegendRewardHandObserved);
        Assert.False(Observe(gate, frame with { RecognitionRevision = 103 }).AwaitingRewardHand);
        var planner = new BeginnerCoachPlanner(_catalog);
        Assert.Equal(CoachActionKind.Story, planner.Decide(frame with { GuidePlan = plan }).Kind);
        Assert.NotEqual(CoachActionKind.Story, planner.Decide(frame with { GuidePlan = plan, Round = 10 }).Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OfferedMultipleAwardsMatchBothOrdersExactlyOnce(bool outputFirst)
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 9, 4, "e016", 3, new());
        Observe(gate, frame, "e016");
        frame = frame with { RecognitionRevision = 2,
            Inventory = outputFirst ? frame.Inventory.Add("rawcode:C10h", 2) : frame.Inventory,
            RewardWisps = outputFirst ? frame.RewardWisps : frame.RewardWisps.SetItem("e016", 1) };
        Observe(gate, frame, "e016");
        frame = frame with { RecognitionRevision = 3, Inventory = frame.Inventory.SetItem("rawcode:C10h", 2),
            RewardWisps = frame.RewardWisps.SetItem("e016", 1) };
        var matched = Observe(gate, frame, "e016");
        Assert.False(matched.AwaitingRewardHand);
        Assert.True(matched.FirstLegendRewardHandObserved);
        frame = frame with { RecognitionRevision = 4, RewardWisps = ImmutableDictionary<string, int>.Empty };
        Assert.True(Observe(gate, frame).AwaitingRewardHand);
        Assert.False(Observe(gate, frame with { RecognitionRevision = 5,
            Inventory = frame.Inventory.SetItem("rawcode:C10h", 3) }).AwaitingRewardHand);
    }

    // seq51-54: two Buggy bodies arrive before the observed e018 decrease of two.
    [Fact]
    public void RecordedSelectedBodiesRemainMatchedWhenOfferBecomesReadyCraft()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(51, 4, 1, "e018", 3, new() { ["rawcode:500h"] = 1 }) with
        { GuidePlan = new(BulletGuideStage.FastUniqueRare, "rawcode:V10h", false) };
        var batch = new SelectionWispBatch("rawcode:V10h", [new("rawcode:500h", 2)]);
        Observe(gate, frame, "e018", batch);
        frame = frame with { RecognitionRevision = 52, Inventory = frame.Inventory.SetItem("rawcode:500h", 2) };
        Observe(gate, frame, "e018", batch with { Items = [new("rawcode:500h", 1)] });
        frame = frame with { RecognitionRevision = 53, Inventory = frame.Inventory.SetItem("rawcode:500h", 3) };
        Observe(gate, frame, craft: true);
        frame = frame with { RecognitionRevision = 54, RewardWisps = frame.RewardWisps.SetItem("e018", 1) };
        var plan = Observe(gate, frame);
        Assert.Equal(0, plan.PendingSelectionOutputs);
        Assert.False(plan.AwaitingRewardHand);
        Assert.Equal(1, frame.RewardWisps["e018"]);
        Assert.Equal("rawcode:V10h", plan.SelectionBatch!.TargetUnitId);
        Assert.Equal(0, plan.SelectionBatch.RemainingCount);
    }

    [Theory]
    [InlineData("e016", "rawcode:N00h")]
    [InlineData("e018", "luffy_common")]
    public void UnrelatedPoolOrSelectedIdentityCannotBecomeEarlyCredit(string reward, string wrong)
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 9, 4, reward, 1, new());
        Observe(gate, frame, reward, new("rawcode:V10h", [new("rawcode:500h", 1)]));
        frame = frame with { RecognitionRevision = 2, Inventory = frame.Inventory.Add(wrong, 1) };
        Observe(gate, frame, reward);
        Assert.True(Observe(gate, frame with { RecognitionRevision = 3,
            RewardWisps = ImmutableDictionary<string, int>.Empty }).AwaitingRewardHand);
    }

    [Theory]
    [InlineData("offer-ended")]
    [InlineData("new-grant")]
    [InlineData("body-lost")]
    [InlineData("new-match")]
    public void CreditsDoNotSurviveTheirReceiptEpisode(string boundary)
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 9, 4, "e016", 1, new());
        Observe(gate, frame, "e016");
        frame = frame with { RecognitionRevision = 2, Inventory = frame.Inventory.Add("rawcode:C10h", 1) };
        Observe(gate, frame, boundary == "offer-ended" ? null : "e016");
        frame = frame with { RecognitionRevision = 3,
            MatchGeneration = boundary == "new-match" ? 43 : 42,
            Inventory = boundary == "body-lost" ? ImmutableDictionary<string, int>.Empty : frame.Inventory,
            RewardWisps = boundary == "new-grant" ? frame.RewardWisps.SetItem("e016", 2) : frame.RewardWisps };
        Observe(gate, frame, "e016");
        Assert.True(Observe(gate, frame with { RecognitionRevision = 4,
            RewardWisps = frame.RewardWisps.SetItem("e016", frame.RewardWisps["e016"] - 1) }).AwaitingRewardHand);
    }

    [Fact]
    public void StaleOutputAndGainsBeforeTheOfferAreNotReceiptEvidence()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 9, 4, "e016", 2, new());
        Observe(gate, frame);
        frame = frame with { RecognitionRevision = 2, Inventory = frame.Inventory.Add("rawcode:C10h", 1) };
        Observe(gate, frame, "e016");
        Observe(gate, frame with { RecognitionRevision = 3, IsCurrent = false,
            Inventory = frame.Inventory.SetItem("rawcode:C10h", 2) }, "e016");
        Assert.True(Observe(gate, frame with { RecognitionRevision = 4,
            RewardWisps = ImmutableDictionary<string, int>.Empty }).AwaitingRewardHand);
    }

    [Fact]
    public void SelectedCreditCannotFollowADifferentTargetOffer()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = Frame(1, 10, 4, "e018", 2, new());
        Observe(gate, frame, "e018", new("rawcode:V10h", [new("rawcode:500h", 1)]));
        frame = frame with { RecognitionRevision = 2, Inventory = frame.Inventory.Add("rawcode:500h", 1) };
        Observe(gate, frame, "e018", new("rawcode:B30h", [new("luffy_common", 1)]));
        Assert.True(Observe(gate, frame with { RecognitionRevision = 3,
            RewardWisps = frame.RewardWisps.SetItem("e018", 1) }).AwaitingRewardHand);
    }

    private static CoachFrame Frame(long revision, int round, int story, string reward, int count,
        Dictionary<string, int> inventory) => new()
    {
        Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 42, Revision = revision,
        RecognitionRevision = revision, Round = round, CompletedStoryStage = story,
        IsCurrent = true, GuideVisible = true, Inventory = inventory.ToImmutableDictionary(),
        RewardWisps = ImmutableDictionary<string, int>.Empty.Add(reward, count),
        GuidePlan = new(BulletGuideStage.FirstLegend, "rawcode:B30h", false)
    };

    private static BulletGuidePlan Observe(FirstLegendRewardGate gate, CoachFrame frame,
        string? reward = null, SelectionWispBatch? batch = null, bool craft = false)
    {
        var plan = gate.Prepare(frame.GuidePlan!, frame);
        gate.Record(frame with { GuidePlan = plan }, new CoachDecision(
            reward is not null ? CoachActionKind.Reward : craft ? CoachActionKind.Craft : CoachActionKind.Story,
            "replay", "", "", "", "", "") { RewardWispId = reward, SelectionBatch = batch });
        return plan;
    }
}
