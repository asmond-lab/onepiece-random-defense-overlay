using System.Collections.Immutable;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ObservedCraftProgressTests
{
    private readonly DataCatalog _catalog = new();
    private readonly CombineHotkeyCatalog _hotkeys = CombineHotkeyCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));

    public ObservedCraftProgressTests()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["leaf"] = new() { Id = "leaf", Name = "leaf", Tier = "흔함" };
        units["other"] = new() { Id = "other", Name = "other", Tier = "흔함" };
        units["copy"] = new() { Id = "copy", Name = "크로", Tier = "안흔함",
            Recipe = new() { ["leaf"] = 2, ["other"] = 1 }, CombineCommands = ["copy"] };
        units["parent"] = new() { Id = "parent", Name = "parent", Tier = "특별함",
            Recipe = new() { ["copy"] = 4 }, CombineCommands = ["parent"] };
    }

    [Fact]
    public void AdditionalCopiesExcludePreownedAndAdvanceAfterObservedRecipes()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        for (var completed = 0; completed < 3; completed++)
        {
            var decision = session.Update(frame);
            Assert.Equal(CoachActionKind.Craft, decision.Kind);
            Assert.Equal("copy", decision.TargetUnitId);
            Assert.Equal(3, decision.CraftProgress!.RequiredCount);
            Assert.Equal(completed, decision.CraftProgress.CompletedCount);
            Assert.Equal(3 - completed, decision.CraftProgress.RemainingCount);
            Assert.False(decision.CraftProgress.AwaitingRecognition);
            Assert.Equal(2, decision.CraftRecipe!.Ingredients.Single(i => i.UnitId == "leaf").RequiredCount);
            frame = Replan(frame with { Revision = frame.Revision + 1, RecognitionRevision = frame.RecognitionRevision + 1,
                Inventory = frame.Inventory.SetItem("copy", 2 + completed).SetItem("leaf", 4 - 2 * completed)
                    .SetItem("other", 2 - completed) });
        }
        Assert.Equal("parent", session.Update(frame).TargetUnitId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SplitReceiptHoldsWithoutDoubleCounting(bool outputFirst)
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        var partial = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            Inventory = outputFirst ? frame.Inventory.SetItem("copy", 2) :
                frame.Inventory.SetItem("leaf", 4).SetItem("other", 2) });
        var pending = session.Update(partial);
        Assert.Equal(CoachActionKind.Waiting, pending.Kind);
        Assert.True(pending.CraftProgress!.AwaitingRecognition);
        Assert.Equal(0, pending.CraftProgress.CompletedCount);
        Assert.Null(pending.CraftRecipe);
        Assert.True(session.Update(partial with { Revision = 3 }).CraftProgress!.AwaitingRecognition);
        var complete = Replan(partial with { Revision = 4, RecognitionRevision = 3,
            Inventory = frame.Inventory.SetItem("copy", 2).SetItem("leaf", 4).SetItem("other", 2) });
        var confirmed = session.Update(complete);
        Assert.Equal(CoachActionKind.Craft, confirmed.Kind);
        Assert.Equal(1, confirmed.CraftProgress!.CompletedCount);
        Assert.Equal(2, confirmed.CraftProgress.RemainingCount);
        Assert.Equal(1, session.Update(complete with { Revision = 5 }).CraftProgress!.CompletedCount);
    }

    [Fact]
    public void UnchangedPausedStaleAndRepeatedRecognitionDoNotCountOrCreatePending()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        Assert.False(session.Update(frame with { Revision = 2, RecognitionRevision = 2 }).CraftProgress!.AwaitingRecognition);
        var changed = Replan(frame with { Revision = 3, RecognitionRevision = 3,
            Inventory = frame.Inventory.SetItem("copy", 2).SetItem("leaf", 4).SetItem("other", 2) });
        Assert.Null(session.Update(changed with { IsCurrent = false }).CraftProgress);
        Assert.Null(session.Update(changed with { Revision = 4, Paused = true }).CraftProgress);
        var forged = session.Update(changed with { Revision = 5, RecognitionRevision = 2 });
        Assert.Equal(0, forged.CraftProgress!.CompletedCount);
        Assert.False(forged.CraftProgress.AwaitingRecognition);
        Assert.Equal(1, session.Update(changed with { Revision = 6 }).CraftProgress!.CompletedCount);
        Assert.Equal(0, session.Update(Frame() with { MatchGeneration = 2 }).CraftProgress!.CompletedCount);
    }

    [Fact]
    public void BatchedCraftReceiptsCountEachRecipeOnce()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        var after = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            Inventory = frame.Inventory.SetItem("copy", 3).SetItem("leaf", 2).SetItem("other", 1) });
        var decision = session.Update(after);
        Assert.Equal(2, decision.CraftProgress!.CompletedCount);
        Assert.Equal(1, decision.CraftProgress.RemainingCount);
        Assert.Equal(decision.CraftProgress, CoachPresentation.Create(decision, after).CraftProgress);
    }

    [Fact]
    public void OutputConsumedIntoParentIsAValidReceiptNotPermanentPending()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        var after = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            Inventory = ImmutableDictionary<string, int>.Empty.Add("parent", 1) });
        var decision = session.Update(after);
        Assert.NotEqual(CoachActionKind.Craft, decision.Kind);
        Assert.False(decision.CraftProgress?.AwaitingRecognition ?? false);
    }

    [Fact]
    public void ChangingPlanClearsPendingEvidence()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        var partial = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            Inventory = frame.Inventory.SetItem("copy", 2) });
        Assert.True(session.Update(partial).CraftProgress!.AwaitingRecognition);
        var reset = session.Update(frame with { Revision = 3, RecognitionRevision = 3, GoalId = "copy" });
        Assert.Equal(0, reset.CraftProgress!.CompletedCount);
        Assert.False(reset.CraftProgress.AwaitingRecognition);
    }

    [Fact]
    public void ConsumedIntermediateStillCountsOnlyNewlyProducedBodies()
    {
        var units = (Dictionary<string, UnitDefinition>)_catalog.UnitsById;
        units["branch"] = new() { Id = "branch", Name = "branch", Tier = "특별함",
            Recipe = new() { ["copy"] = 2 }, CombineCommands = ["branch"] };
        units["parent"].Recipe.Clear();
        units["parent"].Recipe.Add("branch", 1);
        units["parent"].Recipe.Add("copy", 2);
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        var after = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            Inventory = frame.Inventory.SetItem("copy", 1).SetItem("branch", 1).SetItem("leaf", 2).SetItem("other", 1) });
        var decision = session.Update(after);
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal("copy", decision.TargetUnitId);
        Assert.Equal(2, decision.CraftProgress!.CompletedCount);
        Assert.Equal(3, decision.CraftProgress.RequiredCount);
        Assert.Equal(1, decision.CraftProgress.RemainingCount);
    }

    [Fact]
    public void StalePlanChangeCannotDiscardPartialReceipt()
    {
        var session = new BeginnerCoachSession(_catalog);
        var frame = Frame();
        session.Update(frame);
        var partial = Replan(frame with { Revision = 2, RecognitionRevision = 2,
            Inventory = frame.Inventory.SetItem("copy", 2) });
        Assert.True(session.Update(partial).CraftProgress!.AwaitingRecognition);
        session.Update(partial with { Revision = 3, RecognitionRevision = 3, IsCurrent = false, GoalId = "other" });
        Assert.True(session.Update(partial with { Revision = 4, RecognitionRevision = 4 }).CraftProgress!.AwaitingRecognition);
    }

    private CoachFrame Frame() => Replan(new CoachFrame
    {
        Mode = PlayMode.Normal, MatchGeneration = 1, Revision = 1, RecognitionRevision = 1,
        Round = 9, CompletedStoryStage = 7, IsCurrent = true, Difficulty = "신",
        Inventory = ImmutableDictionary<string, int>.Empty.Add("copy", 1).Add("leaf", 6).Add("other", 3)
    });

    private CoachFrame Replan(CoachFrame frame)
    {
        var entries = frame.Inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var recommendations = new RecommendationEngine(_catalog, combineHotkeys: _hotkeys)
            .RecommendNearestCrafts("parent", entries, difficulty: "신", round: 9, completedStoryStage: 7)
            .Where(item => item.Route.GoalUnitId == "parent").ToArray();
        if (frame.Inventory.GetValueOrDefault("parent") == 0) Assert.Single(recommendations);
        return frame with { Recommendations = recommendations,
            CraftSteps = new AutoCombinePlanner(_catalog, _hotkeys).Plan(recommendations, entries) };
    }
}
