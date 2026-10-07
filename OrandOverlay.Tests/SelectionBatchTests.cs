using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class SelectionBatchTests
{
    private readonly DataCatalog _catalog = new();
    public SelectionBatchTests() => _catalog.Load(loadCarryPolicy: false);

    [Fact]
    public void NonurgentSelectionWispsAreConserved()
    {
        Assert.Null(BulletGuideSelectionPolicy.Decide(MissingFrame() with { Round = 9 }, _catalog));
    }

    [Theory]
    [InlineData("e016")]
    [InlineData("e017")]
    [InlineData("e019")]
    [InlineData("e0IX")]
    [InlineData("e01A")]
    public void RandomResultsPrecedeSelections(string randomReward)
    {
        var frame = MissingFrame();
        frame = frame with { RewardWisps = frame.RewardWisps.Add(randomReward, 1) };
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame, _catalog));
        Assert.Equal(randomReward, new BeginnerCoachPlanner(_catalog).Decide(frame).RewardWispId);
    }

    [Fact]
    public void IncompleteBudgetCannotAuthorizePartialSpending()
    {
        Assert.Null(BulletGuideSelectionPolicy.Decide(MissingFrame() with
            { RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 1) }, _catalog));
    }

    [Fact]
    public void ReadyLegendDoesNotRequireSpendingOptionalSelections()
    {
        var frame = MissingFrame() with
        {
            Inventory = _catalog.Unit("rawcode:B30h").Recipe.Where(p => _catalog.Unit(p.Key).Tier != "자원")
                .ToImmutableDictionary()
        };
        var entries = frame.Inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray();
        var recs = new RecommendationEngine(_catalog).RecommendGuideCraft("rawcode:B30h", entries,
            frame.GuidePlan!, frame.Round, frame.CompletedStoryStage);
        var steps = new AutoCombinePlanner(_catalog, CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,
            "Data", "tmo-combine-hotkeys.json"))).Plan(recs, entries);
        var decision = new BeginnerCoachPlanner(_catalog).Decide(frame with { Recommendations = recs, CraftSteps = steps });
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.False(decision.CraftDeferredForReward);
    }

    [Fact]
    public void MinimumSufficientBatchListsEveryPhysicalMissingCommonAndKeepsTheRest()
    {
        var frame = MissingFrame();
        var decision = BulletGuideSelectionPolicy.Decide(frame, _catalog);
        Assert.NotNull(decision);
        Assert.Equal("rawcode:B30h", decision.SelectionBatch!.TargetUnitId);
        Assert.Equal(new[] { new SelectionWispItem("luffy_common", 2), new SelectionWispItem("rawcode:400h", 1) },
            decision.SelectionBatch.Items);
        Assert.Equal(5, decision.SelectionWispsAfterBatch);
        Assert.Equal(8, frame.RewardWisps["e018"]);
    }

    [Fact]
    public void UnknownResourcesAndExplicitReservationsCannotAuthorizePurchases()
    {
        var frame = MissingFrame();
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { Signals = frame.Signals.Remove("lumber") }, _catalog));
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { SelectedGoalIds = ["luffy_common"],
            RewardWisps = frame.RewardWisps.SetItem("e018", 3) }, _catalog));
    }

    [Fact]
    public void SpendingWithoutOutputsHoldsTargetAndDoesNotPromptDuplicateSelections()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = MissingFrame();
        Assert.Equal(3, session.Update(frame).SelectionBatch!.RemainingCount);
        frame = frame with { Revision = 2, RecognitionRevision = 2,
            RewardWisps = frame.RewardWisps.SetItem("e018", 6),
            GuidePlan = frame.GuidePlan! with { TargetUnitId = "rawcode:HA0h" } };
        var pending = session.Update(frame);
        Assert.Equal(CoachActionKind.Waiting, pending.Kind);
        Assert.Equal("rawcode:B30h", pending.SelectionBatch!.TargetUnitId);
        Assert.Equal(3, pending.SelectionBatch.RemainingCount);
        Assert.Null(pending.RewardWispId);
        Assert.Null(pending.SelectionWispsAfterBatch);

        var output = frame with { Revision = 3, RecognitionRevision = 3,
            Inventory = frame.Inventory.SetItem("luffy_common", frame.Inventory["luffy_common"] + 2) };
        Assert.Equal(CoachActionKind.Recognition, session.Update(output with { IsCurrent = false }).Kind);
        Assert.Equal(CoachActionKind.Waiting, session.Update(output with { Paused = true }).Kind);
        var resumed = session.Update(output);
        Assert.Equal(CoachActionKind.Reward, resumed.Kind);
        Assert.Equal("rawcode:B30h", resumed.SelectionBatch!.TargetUnitId);
        Assert.Equal(1, resumed.SelectionBatch.RemainingCount);
        Assert.Equal(5, resumed.SelectionWispsAfterBatch);
        var stale = session.Update(frame with { Revision = 4 });
        Assert.Equal(resumed.SelectionBatch.Items.ToArray(), stale.SelectionBatch!.Items.ToArray());
    }

    [Fact]
    public void AcceptedAcquisitionsDecrementQuantitiesWithoutCountingRepeatedFrames()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = MissingFrame();
        session.Update(frame);
        frame = frame with { Revision = 2, RecognitionRevision = 2,
            RewardWisps = frame.RewardWisps.SetItem("e018", 7),
            Inventory = frame.Inventory.SetItem("luffy_common", frame.Inventory["luffy_common"] + 1) };
        var first = session.Update(frame);
        Assert.Equal(2, first.SelectionBatch!.RemainingCount);
        Assert.Equal(5, first.SelectionWispsAfterBatch);
        Assert.Equal(2, session.Update(frame with { Revision = 3 }).SelectionBatch!.RemainingCount);
        var forged = frame with { Revision = 4, Inventory = frame.Inventory.SetItem("luffy_common", 999) };
        Assert.Equal(2, session.Update(forged).SelectionBatch!.RemainingCount);
        frame = frame with { Revision = 5, RecognitionRevision = 3,
            RewardWisps = frame.RewardWisps.SetItem("e018", 5),
            Inventory = frame.Inventory.SetItem("luffy_common", frame.Inventory["luffy_common"] + 1)
                .SetItem("rawcode:400h", frame.Inventory["rawcode:400h"] + 1) };
        Assert.NotEqual("e018", session.Update(frame).RewardWispId);
        var prepared = session.FirstLegend.Prepare(frame.GuidePlan!, frame);
        Assert.Equal(0, prepared.SelectionBatch!.RemainingCount);
        Assert.Equal(0, prepared.PendingSelectionOutputs);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void FastUniqueRetainsItsExistingQuestDeadline(int round, bool allowed)
    {
        const string rare = "rawcode:E20h";
        var hand = new RecipeCompletionCalculator(_catalog.Unit).Calculate([rare],
            ImmutableDictionary<string, int>.Empty).Leaves.ToImmutableDictionary(leaf => leaf.UnitId,
                leaf => checked((int)leaf.RequiredCount));
        var common = hand.Keys.First(id => _catalog.Unit(id).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains));
        var frame = MissingFrame() with { Round = round, Inventory = hand.SetItem(common, hand[common] - 1),
            GuidePlan = new(BulletGuideStage.FastUniqueRare, rare, false) };
        Assert.Equal(allowed, BulletGuideSelectionPolicy.Decide(frame, _catalog) is not null);
    }

    [Fact]
    public void WrongCommonCannotDischargeASelectedIdentityAndNewMatchResetsTheBatch()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = MissingFrame();
        session.Update(frame);
        frame = frame with { Revision = 2, RecognitionRevision = 2,
            RewardWisps = frame.RewardWisps.SetItem("e018", 7),
            Inventory = frame.Inventory.SetItem("rawcode:100h", frame.Inventory["rawcode:100h"] + 1) };
        var pending = session.Update(frame);
        Assert.Equal(CoachActionKind.Waiting, pending.Kind);
        Assert.Equal(3, pending.SelectionBatch!.RemainingCount);
        var fresh = session.Update(MissingFrame() with { MatchGeneration = 2, Round = 9 });
        Assert.Null(fresh.SelectionBatch);
        Assert.Null(fresh.RewardWispId);
    }

    [Fact]
    public void UnspentRecommendationDoesNotCommitATarget()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = MissingFrame();
        session.Update(frame);
        var next = frame with { Revision = 2, RecognitionRevision = 2,
            GuidePlan = frame.GuidePlan! with { TargetUnitId = "rawcode:HA0h" } };
        var plan = session.FirstLegend.Prepare(next.GuidePlan, next);
        Assert.Null(plan.SelectionBatch);
        Assert.Equal("rawcode:HA0h", plan.TargetUnitId);
    }

    [Fact]
    public void FastUniqueDoesNotSpendSelectionsWhileRandomOutputsArePending()
    {
        var gate = new FirstLegendRewardGate(_catalog);
        var frame = MissingFrame() with { Round = 7,
            GuidePlan = new(BulletGuideStage.FastUniqueRare, "rawcode:E20h", false) };
        frame = frame with { RewardWisps = frame.RewardWisps.Add("e016", 1) };
        gate.Prepare(frame.GuidePlan, frame);
        frame = frame with { Revision = 2, RecognitionRevision = 2, RewardWisps = frame.RewardWisps.Remove("e016") };
        var pending = gate.Prepare(frame.GuidePlan, frame);
        Assert.True(pending.AwaitingRewardHand);
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { GuidePlan = pending }, _catalog));
    }

    [Fact]
    public void StaleCompletedTargetCannotDiscardAnObservedSelectionBatch()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = MissingFrame();
        session.Update(frame);
        frame = frame with { Revision = 2, RecognitionRevision = 2, RewardWisps = frame.RewardWisps.SetItem("e018", 7) };
        session.Update(frame);
        var stale = frame with { IsCurrent = false, RecognitionRevision = 99,
            Inventory = frame.Inventory.SetItem("rawcode:B30h", 1) };
        Assert.NotNull(session.FirstLegend.Prepare(stale.GuidePlan!, stale).SelectionBatch);
        Assert.NotNull(session.FirstLegend.Prepare(frame.GuidePlan!, frame).SelectionBatch);
    }

    [Fact]
    public void HeldBatchCannotBuyCommonsReplacedByAnObservedIntermediate()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = MissingFrame();
        session.Update(frame);
        frame = frame with { Revision = 2, RecognitionRevision = 2, RewardWisps = frame.RewardWisps.SetItem("e018", 7),
            Inventory = frame.Inventory.SetItem("luffy_common", frame.Inventory["luffy_common"] + 1) };
        Assert.Equal(2, session.Update(frame).SelectionBatch!.RemainingCount);
        var recipes = new RecipeCompletionCalculator(_catalog.Unit);
        var replacement = _catalog.AllUnits.First(unit => unit.Recipe.Count > 0 && unit.Id != "rawcode:B30h" &&
            BulletGuideReservations.IsRecipeStep(_catalog, "rawcode:B30h", unit.Id) &&
            recipes.Calculate(["rawcode:B30h"], frame.Inventory.SetItem(unit.Id, 1)).MissingLeaves.Sum(leaf => leaf.MissingCount) == 1);
        frame = frame with { Revision = 3, RecognitionRevision = 3, Inventory = frame.Inventory.SetItem(replacement.Id, 1) };
        var decision = session.Update(frame);
        Assert.Equal(1, decision.SelectionBatch!.RemainingCount);
        Assert.Equal(6, decision.SelectionWispsAfterBatch);
    }

    private CoachFrame MissingFrame()
    {
        var leaves = new RecipeCompletionCalculator(_catalog.Unit).Calculate(["rawcode:B30h"],
            ImmutableDictionary<string, int>.Empty).Leaves;
        var hand = leaves.ToImmutableDictionary(p => p.UnitId, p => checked((int)p.RequiredCount));
        return new CoachFrame
        {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1, RecognitionRevision = 1,
            Round = 10, CompletedStoryStage = 4, IsCurrent = true, Difficulty = "악몽",
            Inventory = hand.SetItem("luffy_common", hand["luffy_common"] - 2)
                .SetItem("rawcode:400h", hand["rawcode:400h"] - 1),
            GuidePlan = new(BulletGuideStage.FirstLegend, "rawcode:B30h", false)
                { FirstLegendRewardHandObserved = true, Round = 10 },
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 8),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100).Add("gold", 100000).Add("trait-points", 100)
        };
    }
}
