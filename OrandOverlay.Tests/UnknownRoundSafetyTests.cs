using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class UnknownRoundSafetyTests
{
    private readonly DataCatalog catalog = new();
    public UnknownRoundSafetyTests() => catalog.Load(loadCarryPolicy: false);

    [Fact]
    public void UnknownRoundDoesNotBecomeFastUniqueDeadlineOrHistoryEvidence()
    {
        var policy = new BulletGuidePolicy(catalog);
        var hand = catalog.Unit("rawcode:L50h").Recipe;
        var plan = policy.Plan(0, 0, hand, "악몽");
        Assert.NotEqual(BulletGuideStage.FastUniqueRare, plan.Stage);
        Assert.Null(plan.TargetUnitId);
        Assert.Equal(FastUniqueState.Unknown, policy.ObserveFastUnique(FastUniqueState.Unknown, 0,
            ImmutableDictionary<string, int>.Empty.Add("rawcode:L50h", 1), true));
        Assert.False(FirstRareRecommendationGate.IsQuestWindow(
            hand.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }), 0));
    }

    [Theory]
    [InlineData(PlayMode.Guide)]
    [InlineData(PlayMode.Manual)]
    public void ReadyInventoryWithUnknownRoundCannotPublishAnAction(PlayMode mode)
    {
        var frame = BeginnerCoachPlannerTests.ReadyFrame() with { Round = 0, Mode = mode,
            GuideNumber = 1, Difficulty = "악몽", GuidePlan = new(BulletGuideStage.FastUniqueRare, "rawcode:L50h", false) };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Recognition, decision.Kind);
        Assert.Null(decision.TargetUnitId);
        Assert.Null(decision.RewardWispId);
        Assert.Null(decision.SelectionBatch);
        Assert.False(decision.RequiresUserConfirmation);
        Assert.Null(decision.MilestoneRound);
    }

    [Fact]
    public void UnknownRoundCannotUseBefore50OrFirstLegendCraftExceptions()
    {
        const string target = "rawcode:B30h";
        var hand = catalog.Unit(target).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원")
            .ToImmutableDictionary();
        var plan = new BulletGuidePlan(BulletGuideStage.FirstLegend, target, false);
        Assert.True(BulletGuideCraftSafety.Allows(catalog, target, hand, 9, null, plan: plan));
        Assert.False(BulletGuideCraftSafety.Allows(catalog, target, hand, 0, null, plan: plan));
    }

    [Fact]
    public void UnknownRoundCannotSpendSelectionWispsEvenWithAStaleFastUniquePlan()
    {
        const string target = "rawcode:L50h";
        var leaves = new RecipeCompletionCalculator(catalog.Unit).Calculate([target],
            ImmutableDictionary<string, int>.Empty).Leaves;
        var hand = leaves.ToImmutableDictionary(leaf => leaf.UnitId, leaf => checked((int)leaf.RequiredCount));
        var common = leaves.First(leaf => catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains)).UnitId;
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 7, IsCurrent = true,
            MatchGeneration = 1, Revision = 1, CompletedStoryStage = 0,
            Difficulty = "악몽", Inventory = hand.SetItem(common, hand[common] - 1),
            GuidePlan = new(BulletGuideStage.FastUniqueRare, target, false) { Round = 7 },
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e018", 1),
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100) };
        Assert.NotNull(BulletGuideSelectionPolicy.Decide(frame, catalog));
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { Round = 0 }, catalog));
        var session = new BeginnerCoachSession(catalog);
        Assert.Equal(CoachActionKind.Reward, session.Update(frame).Kind);
        var unknown = session.Update(frame with { Round = 0, Revision = 2, RecognitionRevision = 2 });
        Assert.Equal(CoachActionKind.Recognition, unknown.Kind);
        Assert.Null(unknown.SelectionBatch);
        Assert.Null(unknown.RewardWispId);
    }

    [Fact]
    public void UnknownRoundCannotExposeAnEarlyGameOperatingBoard()
    {
        var profile = BulletStrategyProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));
        var input = BulletOperatingBoardInput.Unknown(0, BulletGuidePolicy.GoalId, null, "round-source-missing");
        Assert.Null(BulletOperatingBoardPolicy.Evaluate(profile, input));
    }

    [Fact]
    public void UnknownRoundCannotRecordAFirstLegendCraftOffer()
    {
        const string root = "rawcode:B30h";
        var step = catalog.Unit(root).Recipe.Keys.First(id => catalog.Unit(id).Recipe.Count > 0 && catalog.Unit(id).Tier != "자원");
        var hand = catalog.Unit(step).Recipe.Where(pair => catalog.Unit(pair.Key).Tier != "자원").ToImmutableDictionary();
        var plan = new BulletGuidePlan(BulletGuideStage.FirstLegend, root, false);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 0, IsCurrent = true,
            Revision = 1, CompletedStoryStage = 0,
            GuideVisible = true, MatchGeneration = 1, RecognitionRevision = 1, Inventory = hand, GuidePlan = plan };
        var gate = new FirstLegendRewardGate(catalog);
        gate.Prepare(plan, frame);
        gate.Record(frame, new(CoachActionKind.Craft, "craft", "", "", "", "", "") { TargetUnitId = step });
        // Observe the physical result. An unknown-round offer must not establish a commitment.
        var after = frame with { RecognitionRevision = 2, Inventory = ImmutableDictionary<string, int>.Empty.Add(step, 1) };
        gate.Prepare(plan, after);
        Assert.Null(gate.CommittedLegendId);
    }
}
