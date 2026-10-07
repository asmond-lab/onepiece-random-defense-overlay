using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class FirstLegendRewardGateTests
{
    private readonly DataCatalog _catalog = new();

    public FirstLegendRewardGateTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void ReadyFirstLegendWaitsBeforeTenWithoutObservedRewardUse(int completed)
    {
        var frame = Frame(9, completed);

        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);

        Assert.Equal(CoachActionKind.Story, decision.Kind);
        Assert.True(decision.CraftDeferredForReward);
        Assert.Null(decision.CraftRecipe);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(19)]
    public void LateAttachmentDoesNotWaitForAnUnobservedPastReward(int round)
    {
        var decision = new BeginnerCoachPlanner(_catalog).Decide(Frame(round, 0));

        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal("rawcode:B30h", decision.TargetUnitId);
    }

    [Fact]
    public void FourthStoryRewardRequiresWispDecreaseAndFreshResultHand()
    {
        var session = new BeginnerCoachSession(_catalog);
        var received = Replan(Frame(9, 4) with
        { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e016", 2).Add("e017", 1) }, session.FirstLegend);
        Assert.Equal(CoachActionKind.Reward, session.Update(received).Kind);
        var decreased = Replan(received with
        { RecognitionRevision = 2, Revision = 2, RewardWisps = ImmutableDictionary<string, int>.Empty }, session.FirstLegend);
        Assert.True(decreased.GuidePlan!.AwaitingRewardHand);
        Assert.Equal(CoachActionKind.Story, session.Update(decreased).Kind);
        Assert.Equal(CoachActionKind.Story, session.Update(decreased with { Round = 10, Revision = 3 }).Kind);
        var unrelated = Replan(decreased with
        { RecognitionRevision = 3, Revision = 4, Inventory = decreased.Inventory.Add("luffy_common", 1) }, session.FirstLegend);
        Assert.True(unrelated.GuidePlan!.AwaitingRewardHand);
        Assert.False(unrelated.GuidePlan.FirstLegendRewardHandObserved);
        Assert.Equal(CoachActionKind.Story, session.Update(unrelated).Kind);
        var partial = Replan(unrelated with { RecognitionRevision = 4, Revision = 5,
            Inventory = unrelated.Inventory.Add("rawcode:C10h", 1) }, session.FirstLegend);
        Assert.True(partial.GuidePlan!.AwaitingRewardHand);
        var specialsOnly = Replan(partial with { RecognitionRevision = 5, Revision = 6,
            Inventory = partial.Inventory.SetItem("rawcode:C10h", 2) }, session.FirstLegend);
        Assert.True(specialsOnly.GuidePlan!.AwaitingRewardHand);
        var result = Replan(specialsOnly with { RecognitionRevision = 6, Revision = 7,
            Inventory = specialsOnly.Inventory.Add("rawcode:N00h", 1) }, session.FirstLegend);

        Assert.True(result.GuidePlan!.FirstLegendRewardHandObserved);
        Assert.False(result.GuidePlan.AwaitingRewardHand);
        Assert.Equal(CoachActionKind.Craft, session.Update(result).Kind);
        Assert.Null(session.FirstLegend.CommittedLegendId);
    }

    [Fact]
    public void ThirdStoryUseAndFourthStoryZeroDoNotInventFourthRewardReceipt()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var received = Replan(Frame(9, 3) with
        { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e017", 2) }, gate);
        var thirdResult = Replan(received with { RecognitionRevision = 2, Revision = 2,
            RewardWisps = ImmutableDictionary<string, int>.Empty, Inventory = received.Inventory.Add("rawcode:N00h", 2) }, gate);
        var fourth = Replan(thirdResult with { RecognitionRevision = 3, Revision = 3, CompletedStoryStage = 4 }, gate);

        Assert.False(fourth.GuidePlan!.FirstLegendRewardHandObserved);
        Assert.Equal(CoachActionKind.Story, new BeginnerCoachPlanner(_catalog).Decide(fourth).Kind);
    }

    [Theory]
    [InlineData("e016")]
    [InlineData("e017")]
    [InlineData("e018")]
    [InlineData("e019")]
    [InlineData("e0IX")]
    [InlineData("e01A")]
    public void OwnedRandomRewardsWinButOptionalSelectionsDoNotBlockReadyCrafts(string reward)
    {
        var frame = Frame(10, 0) with { RewardWisps = ImmutableDictionary<string, int>.Empty.Add(reward, 1) };

        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame);

        if (reward == "e018")
        {
            Assert.Equal(CoachActionKind.Craft, decision.Kind);
            Assert.Equal("rawcode:B30h", decision.TargetUnitId);
            Assert.False(decision.CraftDeferredForReward);
            return;
        }
        Assert.Equal(CoachActionKind.Reward, decision.Kind);
        Assert.Equal(reward, decision.RewardWispId);
        Assert.Null(decision.CraftRecipe);
        Assert.True(decision.CraftDeferredForReward);
    }

    [Fact]
    public void UnsupportedConsumptionCannotCreatePermanentDebtOrCompleteCheckpoint()
    {
        foreach (var id in new[] { "e0IX", "e01A" })
        {
            var session = new BeginnerCoachSession(_catalog);
            var received = Replan(Frame(9, 4) with
            { RewardWisps = ImmutableDictionary<string, int>.Empty.Add(id, 1) }, session.FirstLegend);
            Assert.Equal(CoachActionKind.Reward, session.Update(received).Kind);
            var spent = Replan(received with { RecognitionRevision = 2, Revision = 2,
                RewardWisps = ImmutableDictionary<string, int>.Empty }, session.FirstLegend);
            Assert.False(spent.GuidePlan!.AwaitingRewardHand);
            Assert.False(spent.GuidePlan.FirstLegendRewardHandObserved);
            Assert.Equal(CoachActionKind.Story, session.Update(spent).Kind);
            var unrelated = Replan(spent with { RecognitionRevision = 3, Revision = 3,
                Inventory = spent.Inventory.Add("rawcode:C10h", 1) }, session.FirstLegend);
            Assert.False(unrelated.GuidePlan!.FirstLegendRewardHandObserved);

            var deadline = Replan(unrelated with { RecognitionRevision = 4, Revision = 4, Round = 10 }, session.FirstLegend);

            Assert.False(deadline.GuidePlan!.AwaitingRewardHand);
            Assert.False(deadline.GuidePlan.FirstLegendRewardHandObserved);
            Assert.Equal(CoachActionKind.Craft, session.Update(deadline).Kind);
        }
    }

    [Fact]
    public void StaleRewardResultsAndNewMatchesCannotReleaseGate()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var received = Replan(Frame(9, 4) with
        { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e016", 1) }, gate);
        var result = received with { RecognitionRevision = 2, Revision = 2, RewardWisps = ImmutableDictionary<string, int>.Empty,
            Inventory = received.Inventory.Add("rawcode:C10h", 1) };
        Assert.False(Replan(result with { IsCurrent = false }, gate).GuidePlan!.FirstLegendRewardHandObserved);
        Assert.False(Replan(result with { Paused = true }, gate).GuidePlan!.FirstLegendRewardHandObserved);
        Assert.True(Replan(result, gate).GuidePlan!.FirstLegendRewardHandObserved);

        var reset = Replan(result with { MatchGeneration = 2, RecognitionRevision = 3 }, gate);

        Assert.False(reset.GuidePlan!.FirstLegendRewardHandObserved);
        Assert.Null(gate.CommittedLegendId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(8, true)]
    [InlineData(8, false)]
    public void RewardedSequence365HandCompletesAllSevenCraftsWithoutRecommendationLocking(int retainedSelections, bool? outputFirst = null)
    {
        var session = new BeginnerCoachSession(_catalog);
        var before = RecordedHand();
        Assert.Equal("특별함", TopGradePolicy.BaseTier(_catalog.Unit("rawcode:C10h").Tier));
        var hand = before.SetItem("rawcode:C10h", before["rawcode:C10h"] + 1);
        var frame = Replan(Frame(9, 4) with { Inventory = before,
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e016", 1).Add("e018", retainedSelections) }, session.FirstLegend);
        Assert.Equal(CoachActionKind.Reward, session.Update(frame).Kind);
        frame = Replan(frame with { Inventory = hand, RewardWisps = frame.RewardWisps.Remove("e016"),
            RecognitionRevision = 2, Revision = 2 }, session.FirstLegend);
        string[] expected = ["F00h", "V00h", "V00h", "220h", "E20h", "J20h", "B30h"];
        for (var index = 0; index < expected.Length; index++)
        {
            var decision = session.Update(frame);
            Assert.Equal(CoachActionKind.Craft, decision.Kind);
            Assert.Equal("rawcode:" + expected[index], decision.TargetUnitId);
            Assert.NotNull(decision.CraftProgress);
            Assert.Equal(index == 2 ? 1 : 0, decision.CraftProgress.CompletedCount);
            Assert.Equal(index is 1 or 2 ? 2 : 1, decision.CraftProgress.RequiredCount);
            Assert.Equal(retainedSelections, frame.RewardWisps["e018"]);
            Assert.Equal(index == 0 ? null : "rawcode:B30h", session.FirstLegend.CommittedLegendId);
            var after = BulletGuideCraftSafety.ProjectAfterCraft(_catalog, decision.TargetUnitId!, frame.Inventory)!;
            if (outputFirst is { } first)
            {
                var partialInventory = first ? frame.Inventory.SetItem(decision.TargetUnitId!,
                    frame.Inventory.GetValueOrDefault(decision.TargetUnitId!) + 1) :
                    after.ToImmutableDictionary().SetItem(decision.TargetUnitId!, frame.Inventory.GetValueOrDefault(decision.TargetUnitId!));
                var partial = Replan(frame with { Inventory = partialInventory,
                    RecognitionRevision = frame.RecognitionRevision + 1, Revision = frame.Revision + 1 }, session.FirstLegend);
                var pending = session.Update(partial);
                Assert.True(pending.CraftProgress?.AwaitingRecognition);
                Assert.NotEqual(CoachActionKind.Craft, pending.Kind);
                frame = partial;
            }
            frame = Replan(frame with { Inventory = after.Where(pair => pair.Value > 0).ToImmutableDictionary(),
                RecognitionRevision = frame.RecognitionRevision + 1, Revision = frame.Revision + 1 }, session.FirstLegend);
            if (index < expected.Length - 1)
            {
                Assert.Equal("rawcode:B30h", session.FirstLegend.CommittedLegendId);
                var reranked = session.FirstLegend.Prepare(frame.GuidePlan! with { TargetUnitId = "rawcode:HA0h" }, frame);
                Assert.Equal("rawcode:B30h", reranked.TargetUnitId);
            }
        }
        Assert.Equal(1, frame.Inventory["rawcode:B30h"]);
        Assert.Equal(BulletGuideStage.SecondLegend, frame.GuidePlan!.Stage);
        Assert.Null(session.FirstLegend.CommittedLegendId);
    }

    [Fact]
    public void MaterialLossReleasesAnObservedIntermediateCommitment()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Replan(Frame(10, 0) with { Inventory = RecordedHand() }, session.FirstLegend);
        var decision = session.Update(frame);
        var after = BulletGuideCraftSafety.ProjectAfterCraft(_catalog, decision.TargetUnitId!, frame.Inventory)!;
        frame = Replan(frame with { Inventory = after.Where(pair => pair.Value > 0).ToImmutableDictionary(),
            RecognitionRevision = 2, Revision = 2 }, session.FirstLegend);
        Assert.Equal("rawcode:B30h", session.FirstLegend.CommittedLegendId);

        Replan(frame with { Inventory = frame.Inventory.Remove("rawcode:F00h"), RecognitionRevision = 3, Revision = 3 }, session.FirstLegend);

        Assert.Null(session.FirstLegend.CommittedLegendId);
    }

    [Fact]
    public void UnconsumedIngredientsDoNotTurnAnOutputIncreaseIntoCraftCommitment()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Replan(Frame(10, 0) with { Inventory = RecordedHand() }, session.FirstLegend);
        var decision = session.Update(frame);

        Replan(frame with { Inventory = frame.Inventory.SetItem(decision.TargetUnitId!, frame.Inventory[decision.TargetUnitId!] + 1),
            RecognitionRevision = 2, Revision = 2 }, session.FirstLegend);

        Assert.Null(session.FirstLegend.CommittedLegendId);
    }

    private static ImmutableDictionary<string, int> RecordedHand() => new Dictionary<string, int>
    {
        ["luffy_common"] = 3, ["rawcode:320h"] = 1, ["rawcode:N00h"] = 2, ["rawcode:J10h"] = 1,
        ["rawcode:U10h"] = 1, ["rawcode:R00h"] = 1, ["rawcode:500h"] = 3, ["rawcode:210h"] = 1,
        ["rawcode:200h"] = 3, ["rawcode:C00h"] = 1, ["rawcode:F00h"] = 1, ["rawcode:D20h"] = 1,
        ["rawcode:800h"] = 1, ["rawcode:410h"] = 1, ["rawcode:700h"] = 3, ["rawcode:600h"] = 2,
        ["rawcode:X00h"] = 1, ["rawcode:110h"] = 1, ["rawcode:L50h"] = 2, ["rawcode:900h"] = 1,
        ["rawcode:C10h"] = 1, ["rawcode:D10h"] = 1, ["rawcode:100h"] = 1, ["rawcode:B10h"] = 1,
        ["rawcode:W00h"] = 1, ["rawcode:400h"] = 5, ["rawcode:G20h"] = 2, ["rawcode:D00h"] = 1,
        ["rawcode:910h"] = 1, ["rawcode:T00h"] = 1, ["rawcode:E00h"] = 2
    }.ToImmutableDictionary();

    private CoachFrame Frame(int round, int completed)
    {
        return Replan(new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, GoalId = BulletGuidePolicy.GoalId,
            MatchGeneration = 1, Revision = 1, RecognitionRevision = 1, Round = round, CompletedStoryStage = completed,
            IsCurrent = true, Difficulty = "악몽", Inventory = _catalog.Unit("rawcode:B30h").Recipe
                .Where(pair => _catalog.Unit(pair.Key).Tier != "자원").ToImmutableDictionary(),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 16)
        });
    }

    private CoachFrame Replan(CoachFrame frame, FirstLegendRewardGate? gate = null)
    {
        var (round, completed, inventory) = (frame.Round, frame.CompletedStoryStage, frame.Inventory);
        var entries = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var plan = new BulletGuidePolicy(_catalog).Plan(round, completed, inventory, "악몽");
        if (gate is not null) plan = gate.Prepare(plan, frame);
        var story = StoryRewardSequencePlanner.Evaluate(new StoryRewardSequenceInput
        {
            Phase = PlannerPhase.AwaitFirstRare, Round = round, ActiveStoryStage = completed + 1,
            CompletedStoryStage = completed, RewardWisps = frame.RewardWisps,
            Inventory = entries, Units = _catalog.UnitsById,
            StoryStages = MapStoryProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data")).Stages
        });
        var recommendations = new RecommendationEngine(_catalog).RecommendGuideCraft(plan.TargetUnitId!, entries, plan, round, completed);
        var steps = new AutoCombinePlanner(_catalog, CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,
            "Data", "tmo-combine-hotkeys.json"))).Plan(recommendations, entries);
        return frame with
        {
            GuidePlan = plan, Story = story, Recommendations = recommendations, CraftSteps = steps
        };
    }
}
