using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletGuideCompletionTests
{
    [Theory]
    [InlineData(11, 3, false)]
    [InlineData(11, 12, false)]
    [InlineData(12, 3, true)]
    [InlineData(12, 12, true)]
    public void FirstLegendRecoveryUsesRoundNotStory(int round, int story, bool overdue)
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add("luffy_common", 1);
        var plan = new BulletGuidePolicy(catalog).Plan(round, story, inventory, "악몽");
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = round,
            CompletedStoryStage = story, IsCurrent = true, Difficulty = "악몽",
            Inventory = inventory, GuidePlan = plan, MatchGeneration = 1, Revision = 1 };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(BulletGuideStage.FirstLegend, plan.Stage);
        Assert.Equal(overdue, decision.Milestone.Contains("12라 목표 경과"));
        if (overdue) Assert.Contains("재료 보존", decision.Milestone);
        Assert.NotEqual(CoachActionKind.Finished, decision.Kind);
    }

    [Fact]
    public void DuplicateConsiderationYieldsToObservedUncommonSaleBeforeUpgrade()
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes().Concat(new[] { "320h", "320h", "A00h" }).ToArray(),
            BulletGuideUncommonSaleTests.Frame().CombatObservations[0]);
        frame = frame with { GuideRuntime = new BulletGuideRuntimeState(true, 2, 1, 1, "fixture")
            .WithExactCounts(new(1, 1, 15)) };
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("guide1:sell-uncommon:rawcode:A00h", decision.Id);
    }

    [Fact]
    public void ProtectedRecipeIngredientsAreNotDuplicateSaleSurplus()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes().Concat(new[] { "D20h", "Y00h" }).ToArray());
        var protectedRecipe = catalog.AllUnits.First(unit => unit.Recipe.ContainsKey("rawcode:Y00h") &&
            !frame.Inventory.ContainsKey(unit.Id));
        frame = frame with { GuidePlan = frame.GuidePlan! with { ProtectedUnitIds = [protectedRecipe.Id] } };
        var allocation = new RecipeCompletionCalculator(catalog.Unit)
            .CalculateAllocation([protectedRecipe.Id], frame.Inventory);
        Assert.Equal(0, allocation.RemainingInventory.GetValueOrDefault("rawcode:Y00h"));
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.NotEqual("guide1:sell-special:rawcode:Y00h", decision.Id);
    }

    [Fact]
    public void ProtectedCommonPackageCannotBeSpentOnUpgrade()
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes());
        var common = frame.Inventory.Keys.Single(id =>
            BulletGuideUncommonSaleTests.Catalog().Unit(id).Rawcodes.Contains("300h"));
        frame = frame with { Inventory = frame.Inventory.SetItem(common, 1),
            GuidePlan = frame.GuidePlan! with { ProtectedUnitIds = [common] },
            GuideRuntime = new BulletGuideRuntimeState(true, 2, 1, 1, "fixture").WithExactCounts(new(1, 1, 15)) };
        Assert.True(frame.GuidePlan.Support!.IsReady);
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.NotEqual(CoachActionKind.Upgrade, decision.Kind);
        Assert.Equal(1, frame.Inventory[common]);
    }

    [Fact]
    public void OptionalDuplicateConsiderationDoesNotStarveObservedUpgrade()
    {
        var frame = BulletGuideUncommonSaleTests.PlannedFrame(
            BulletGuideUncommonSaleTests.OperatingSupportCodes().Concat(new[] { "320h", "320h" }).ToArray());
        frame = frame with { GuideRuntime = new BulletGuideRuntimeState(true, 2, 1, 1, "fixture")
            .WithExactCounts(new(1, 1, 15)) };
        Assert.True(frame.GuidePlan!.Support!.IsReady);
        Assert.Empty(frame.RewardWisps);
        Assert.NotNull(BulletGuideBlackMariaPolicy.Consider(frame, BulletGuideUncommonSaleTests.Catalog()));
        var decision = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("guide1:upgrade:armor", decision.Id);
        Assert.Equal(CoachActionKind.Upgrade, decision.Kind);
        Assert.Equal(2, frame.Inventory["rawcode:320h"]);
    }
}
