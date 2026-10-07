using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletGuideSecondCompletionTests
{
    [Theory]
    [InlineData(QueenConversionInput.UserConfirmedMissionsComplete)]
    [InlineData(QueenConversionInput.UserConfirmedStoryTooSlow)]
    public void ExplicitQueenConditionFlowsThroughActualRecipeAndPlanner(QueenConversionInput input)
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "HA0h", "I20h", "L00h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(25, 10, inventory, "악몽",
            BulletGuidePolicy.NavigationId, queenInput: input);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 25,
            CompletedStoryStage = 10, IsCurrent = true, Difficulty = "악몽", Inventory = inventory,
            GuidePlan = plan, MatchGeneration = 1, Revision = 1,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId };
        Assert.Equal("rawcode:IC0h", plan.TargetUnitId);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var entries = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 25, CompletedStoryStage = 10
        });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries);
        Assert.Contains(steps, step => step.TargetUnitId == "rawcode:IC0h");
        frame = frame with { CraftSteps = steps, Recommendations = candidates.Recommendations,
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100) };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal("rawcode:IC0h", decision.TargetUnitId);
        Assert.Contains("사용자 확인", decision.OperationGuide);
        Assert.Equal(1, plan.AirCount); // Never pre-credit the uncrafted Queen.
        Assert.False(plan.Support!.StunPairReady);
    }

    [Fact]
    public void BothCoachViewsProduceExplicitQueenInputAndResetItAtMatchBoundary()
    {
        var ui = GoroseiProducerTests.Source("BeginnerCoachView.xaml");
        Assert.Contains("coach-queen-condition", ui);
        var view = GoroseiProducerTests.Source("BeginnerCoachView.xaml.cs");
        Assert.Contains("QueenConditionRequested?.Invoke", view);
        var main = GoroseiProducerTests.Source("MainWindow.Coach.cs");
        Assert.Contains("view.QueenConditionRequested +=", main);
        Assert.Contains("_coachCurrent && !_coachPaused", main);
        var source = GoroseiProducerTests.Source("MainWindow.xaml.cs");
        Assert.Contains("queenInput: _coachCurrent ? _queenInput : QueenConversionInput.Unknown", source);
        Assert.Contains("_queenInput = QueenConversionInput.Unknown;", source[source.IndexOf("private void ResetMatchSession")..]);
    }

    [Theory]
    [InlineData(QueenConversionInput.Unknown)]
    [InlineData(QueenConversionInput.KeepKing)]
    public void UnconfirmedQueenNeverUsesStoryStageAsMissionCompletion(QueenConversionInput input)
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "HA0h", "I20h", "L00h", "180h", "U30h", "540h" }
            .ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(60, 13, inventory, "악몽",
            BulletGuidePolicy.NavigationId, queenInput: input);
        Assert.NotEqual("rawcode:IC0h", plan.TargetUnitId);
        Assert.False(plan.QueenConversionConfirmed);
    }

    [Fact]
    public void MarcoMaterialsReserveJointWhitebeardHibariBonClayPackage()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "220h", "M20h", "M10h", "N10h", "P10h" }
            .ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(11, 5, inventory, "악몽");
        Assert.Contains("rawcode:B30h", plan.ProtectedUnitIds);
        Assert.Contains("rawcode:MC0h", plan.ProtectedUnitIds);
        Assert.Contains("rawcode:O30h", plan.ProtectedUnitIds);
        Assert.Equal("rawcode:MC0h", plan.TargetUnitId);
        var allocation = new RecipeCompletionCalculator(catalog.Unit)
            .CalculateAllocation(plan.ProtectedUnitIds, inventory);
        foreach (var code in new[] { "220h", "M20h", "M10h" })
            Assert.Equal(0, allocation.RemainingInventory.GetValueOrDefault("rawcode:" + code));
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 11,
            CompletedStoryStage = 5, IsCurrent = true, Difficulty = "악몽", Inventory = inventory,
            GuidePlan = plan, MatchGeneration = 1, Revision = 1 };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
        Assert.Equal(1, inventory["rawcode:220h"]);
    }

    [Fact]
    public void ReadyAceCannotStealReservedWhitebeardMaterial()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "220h", "Y10h", "120h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(11, 5, inventory, "악몽");
        Assert.Contains("rawcode:B30h", plan.ProtectedUnitIds);
        Assert.NotEqual("rawcode:O20h", plan.TargetUnitId);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var entries = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 11, CompletedStoryStage = 5
        });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        Assert.DoesNotContain(steps, step => catalog.Unit(step.TargetUnitId).Recipe.ContainsKey("rawcode:220h"));
    }

    [Fact]
    public void RecipeConsumerCannotBypassJointPackageReservation()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "220h", "Y10h", "120h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var sourcePlan = new BulletGuidePolicy(catalog).Plan(11, 5, inventory, "악몽");
        var plan = sourcePlan with { TargetUnitId = "rawcode:O20h" }; // Competing/previous candidate.
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var entries = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 11, CompletedStoryStage = 5
        });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        Assert.DoesNotContain(steps, step => step.TargetUnitId == "rawcode:O20h");
        Assert.True(Assert.Single(candidates.Recommendations).RecipeProgress.CompletionRatio < 1);
    }

    [Fact]
    public void ObservedTwoKingsRemainHistoricalLowerBoundAfterConversion()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "IC0h", "MC0h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(17, 9, inventory, "악몽",
            observedLegendIds: ["rawcode:HA0h"],
            observedLegendCounts: new Dictionary<string, int> { ["rawcode:HA0h"] = 2 });
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 17,
            CompletedStoryStage = 9, IsCurrent = true, Difficulty = "악몽", Inventory = inventory,
            GuidePlan = plan, MatchGeneration = 1, Revision = 1 };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(BulletGuideStage.FourthLegendReward, plan.Stage);
        Assert.Equal(3, plan.KnownLegendLowerBound);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
        Assert.Equal(0, plan.AirCount); // History cannot supply present mobility.
    }

    [Fact]
    public void AcceptedInventoryProducerRetainsPeakCountsAndClearsAtBoundary()
    {
        var coach = GoroseiProducerTests.Source("MainWindow.Coach.cs");
        Assert.Contains("_guideObservedLegendCounts", coach);
        Assert.Contains("Math.Max(_guideObservedLegendCounts.GetValueOrDefault", coach);
        var main = GoroseiProducerTests.Source("MainWindow.xaml.cs");
        Assert.Contains("observedLegendCounts: _guideObservedLegendCounts", main);
        Assert.Contains("_guideObservedLegendCounts.Clear();", main[main.IndexOf("private void ResetMatchSession")..]);
    }

    [Fact]
    public void FinalCoachRechecksReservationsInsteadOfTrustingPreviousCraftStep()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "220h", "Y10h", "120h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(11, 5, inventory, "악몽");
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 11,
            CompletedStoryStage = 5, IsCurrent = true, Difficulty = "악몽", Inventory = inventory,
            GuidePlan = plan, MatchGeneration = 1, Revision = 1,
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100),
            CraftSteps = [new("rawcode:O20h", "에이스", "rawcode:220h", "마르코", "220h", "X", [])] };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.NotEqual(CoachActionKind.Craft, decision.Kind);
        Assert.Equal("guide1:reservation:rawcode:O20h", decision.Id);
        Assert.Equal("rawcode:O20h", decision.TargetUnitId);
    }

    [Fact]
    public void ReadyEarlyStunComplementDoesNotWaitForAllArmorAndSlow()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "HA0h", "U30h", "MC0h", "W20h", "U20h", "V20h", "930h",
            "M10h", "410h", "510h", "X00h", "G20h", "J20h", "720h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(40, 13, inventory, "악몽", BulletGuidePolicy.NavigationId);
        Assert.False(plan.Support!.StunPairReady);
        Assert.True(plan.Support.ArmorPotential < plan.Support.ArmorTarget);
        Assert.Equal("rawcode:O30h", plan.TargetUnitId);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var entries = inventory.Select(pair => new InventoryEntry { UnitId = pair.Key, Count = pair.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest
        {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries,
            InitialSurface = RecommendationSurface.TopAndNavigation, NavigationMode = BulletGuidePolicy.NavigationId,
            Gorosei = GoroseiMode.None, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = 40, CompletedStoryStage = 13
        });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 40,
            CompletedStoryStage = 13, IsCurrent = true, Difficulty = "악몽", Inventory = inventory,
            GuidePlan = plan, MatchGeneration = 1, Revision = 1, ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            CraftSteps = steps, Recommendations = candidates.Recommendations,
            Signals = ImmutableDictionary<string, long?>.Empty.Add("lumber", 100) };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal("rawcode:O30h", decision.TargetUnitId);
        Assert.Contains("준비된 스턴", decision.Milestone);
    }

    [Fact]
    public void DestructionRaceCannotStealJointPackageForReadyFourthLegend()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "HA0h", "U30h", "MC0h", "220h", "Y10h", "120h" }
            .ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(29, 10, inventory, "악몽",
            BulletGuidePolicy.NavigationId, destructionKingAvailable: true);
        Assert.True(plan.PursueDestructionKing);
        Assert.Contains("rawcode:B30h", plan.ProtectedUnitIds);
        Assert.NotEqual("rawcode:O20h", plan.TargetUnitId);
    }

    [Fact]
    public void ArmorOnlyOpeningRepairsSourceRoleBeforeThirdAirTarget()
    {
        var catalog = BulletGuideUncommonSaleTests.Catalog();
        var inventory = new[] { "H30h", "S30h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var plan = new BulletGuidePolicy(catalog).Plan(17, 9, inventory, "악몽");
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = 17,
            CompletedStoryStage = 9, IsCurrent = true, Difficulty = "악몽", Inventory = inventory,
            GuidePlan = plan, MatchGeneration = 1, Revision = 1 };
        var decision = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal("rawcode:MC0h", decision.TargetUnitId);
        Assert.Contains("초반 역할 보완", decision.Milestone);
        Assert.Equal(2, plan.KnownLegendLowerBound);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
    }
}
