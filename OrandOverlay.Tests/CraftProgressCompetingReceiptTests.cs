using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CraftProgressCompetingReceiptTests
{
    private readonly DataCatalog _catalog = new();
    private readonly CombineHotkeyCatalog _hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
    public CraftProgressCompetingReceiptTests() => _catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservedRewardOutputDoesNotLeaveAnOldCraftPendingAfterWispConsumption(bool spent)
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        var offered = session.Update(frame);
        Assert.Equal(CoachActionKind.Craft, offered.Kind);
        var unit = _catalog.Unit(offered.TargetUnitId!);
        Assert.Equal("안흔함", TopGradePolicy.BaseTier(unit.Tier));
        var reward = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e017", 1),
            Story = new(RecommendationSequenceStage.StoryReward, StorySequenceAction.SpendStoryWisps, "", "", "", "", "", null, null, 0, false),
            Inventory = frame.Inventory.SetItem(unit.Id, frame.Inventory.GetValueOrDefault(unit.Id) + 1) });
        Assert.Equal(CoachActionKind.Reward, session.Update(reward).Kind);
        var settled = Replan(reward with { Revision = 3, RecognitionRevision = 3,
            RewardWisps = spent ? ImmutableDictionary<string, int>.Empty : reward.RewardWisps, Story = null });
        var decision = session.Update(settled);
        if (!spent)
        {
            Assert.True(decision.CraftProgress!.AwaitingRecognition);
            Assert.Equal(0, decision.CraftProgress.CompletedCount);
            return;
        }
        Assert.Equal(new BeginnerCoachPlanner(_catalog).Decide(settled).TargetUnitId, decision.TargetUnitId);
        Assert.False(decision.CraftProgress?.AwaitingRecognition ?? false);
        Assert.Equal(0, decision.CraftProgress?.CompletedCount ?? 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConfirmedDifferentRecipeSharingInputsRetiresOnlyItsOwnEvidence(int ordering)
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        var offered = session.Update(frame);
        Assert.Equal(CoachActionKind.Craft, offered.Kind);
        var recipe = _catalog.Unit(offered.TargetUnitId!).Recipe;
        var alternative = _catalog.AllUnits.First(unit => unit.Id != offered.TargetUnitId && unit.Recipe.Count > 0 &&
            unit.Recipe.Keys.Any(recipe.ContainsKey) && !unit.Recipe.ContainsKey(offered.TargetUnitId!) &&
            unit.Recipe.All(pair => _catalog.Unit(pair.Key).Tier != "자원" && frame.Inventory.GetValueOrDefault(pair.Key) >= pair.Value));
        var after = frame.Inventory.SetItem(alternative.Id, frame.Inventory.GetValueOrDefault(alternative.Id) + 1);
        foreach (var pair in alternative.Recipe) after = after.SetItem(pair.Key, after.GetValueOrDefault(pair.Key) - pair.Value);
        if (ordering != 0)
        {
            var partial = ordering == 1 ? after.SetItem(alternative.Id, frame.Inventory.GetValueOrDefault(alternative.Id)) :
                frame.Inventory.SetItem(alternative.Id, frame.Inventory.GetValueOrDefault(alternative.Id) + 1);
            session.Update(Replan(frame with { Revision = 2, RecognitionRevision = 2, Inventory = partial }));
        }
        var settled = Replan(frame with { Revision = 3, RecognitionRevision = 3, Inventory = after });
        Assert.Equal(frame.Recommendations[0].Route.GoalUnitId, settled.Recommendations[0].Route.GoalUnitId);
        var expected = new BeginnerCoachPlanner(_catalog).Decide(settled);
        Assert.Equal(CoachActionKind.Craft, expected.Kind);
        var actual = session.Update(settled);
        Assert.Equal(CoachActionKind.Craft, actual.Kind);
        Assert.Equal(expected.TargetUnitId, actual.TargetUnitId);
        Assert.False(actual.CraftProgress!.AwaitingRecognition);
        Assert.Equal(0, actual.CraftProgress.CompletedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternativeReceiptCannotEraseARealPartialCraft(bool outputFirst)
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        var offered = session.Update(frame);
        var recipe = _catalog.Unit(offered.TargetUnitId!).Recipe;
        var alternative = _catalog.AllUnits.First(unit => unit.Id != offered.TargetUnitId && unit.Recipe.Count > 0 &&
            unit.Recipe.Keys.Any(recipe.ContainsKey) && !unit.Recipe.ContainsKey(offered.TargetUnitId!) &&
            unit.Recipe.All(pair => _catalog.Unit(pair.Key).Tier != "자원" && frame.Inventory.GetValueOrDefault(pair.Key) >= pair.Value));
        var other = frame.Inventory.SetItem(alternative.Id, frame.Inventory.GetValueOrDefault(alternative.Id) + 1);
        foreach (var pair in alternative.Recipe) other = other.SetItem(pair.Key, other.GetValueOrDefault(pair.Key) - pair.Value);
        var partial = outputFirst ? other.SetItem(offered.TargetUnitId!, other.GetValueOrDefault(offered.TargetUnitId!) + 1) : other;
        if (!outputFirst)
            foreach (var pair in recipe) partial = partial.SetItem(pair.Key, partial.GetValueOrDefault(pair.Key) - pair.Value);
        var waiting = session.Update(Replan(frame with { Revision = 2, RecognitionRevision = 2, Inventory = partial }));
        Assert.True(waiting.CraftProgress!.AwaitingRecognition);
        Assert.Equal(0, waiting.CraftProgress.CompletedCount);
        var complete = other.SetItem(offered.TargetUnitId!, other.GetValueOrDefault(offered.TargetUnitId!) + 1);
        foreach (var pair in recipe) complete = complete.SetItem(pair.Key, complete.GetValueOrDefault(pair.Key) - pair.Value);
        var result = session.Update(Replan(frame with { Revision = 3, RecognitionRevision = 3, Inventory = complete }));
        Assert.False(result.CraftProgress?.AwaitingRecognition ?? false);
        Assert.Equal(CoachActionKind.Craft, result.Kind);
        var duplicate = session.Update(Replan(frame with { Revision = 4, RecognitionRevision = 4, Inventory = complete }));
        Assert.Equal(result.CraftProgress, duplicate.CraftProgress);
    }

    private CoachFrame Frame()
    {
        var inventory = new RecipeCompletionCalculator(_catalog.Unit).Calculate(["rawcode:B30h"], ImmutableDictionary<string, int>.Empty)
            .MissingLeaves.ToImmutableDictionary(pair => pair.UnitId, pair => checked((int)pair.MissingCount * 3));
        return Replan(new CoachFrame { Mode = PlayMode.Normal, MatchGeneration = 1, Revision = 1, RecognitionRevision = 1,
            Round = 9, CompletedStoryStage = 7, IsCurrent = true, Difficulty = "신", Inventory = inventory });
    }

    private CoachFrame Replan(CoachFrame frame)
    {
        var entries = frame.Inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var recommendations = new RecommendationEngine(_catalog, combineHotkeys: _hotkeys)
            .RecommendNearestCrafts("rawcode:B30h", entries, difficulty: "신", round: 9, completedStoryStage: 7)
            .Where(item => item.Route.GoalUnitId == "rawcode:B30h").ToArray();
        Assert.Single(recommendations);
        return frame with { Recommendations = recommendations, CraftSteps = new AutoCombinePlanner(_catalog, _hotkeys).Plan(recommendations, entries) };
    }
}
