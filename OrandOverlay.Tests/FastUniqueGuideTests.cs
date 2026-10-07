using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

// Synthetic catalog hand, NOT the user's unrecorded starting hand.
public sealed class FastUniqueGuideTests
{
    private readonly DataCatalog catalog = new();
    public FastUniqueGuideTests() => catalog.Load(loadCarryPolicy: false);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void BeforeEightHasExplicitQuestMilestoneNotFirstLegend(int round)
    {
        var plan = new BulletGuidePolicy(catalog).Plan(round, 0,
            catalog.Unit("rawcode:L50h").Recipe, "악몽");
        if (round == 0)
        {
            Assert.Equal(BulletGuideStage.RoundUnknown, plan.Stage);
            Assert.Null(plan.TargetUnitId);
            return;
        }
        Assert.Equal(BulletGuideStage.FastUniqueRare, plan.Stage);
        Assert.Equal(FastUniqueState.Unknown, plan.FastUnique);
        Assert.Equal(round, plan.Round);
        Assert.Equal("희귀함", catalog.Unit(plan.TargetUnitId!).Tier);
    }

    [Fact]
    public void EightRestoresHiddenOpeningAndTwelveLegendDeadline()
    {
        var inventory = catalog.Unit("rawcode:L50h").Recipe;
        var plan = new BulletGuidePolicy(catalog).Plan(8, 0, inventory, "악몽");
        Assert.Equal(BulletGuideStage.FirstLegend, plan.Stage);
        Assert.Equal("rawcode:M30h", plan.TargetUnitId);
        Assert.Contains("12라", BulletGuideAdvice.Stage(plan));
    }

    [Theory]
    [InlineData(1)] [InlineData(3)]
    public void SelectionConservesInsufficientBudgetBeforeAndAtLegendUrgency(int wisps)
    {
        var plan = new BulletGuidePlan(BulletGuideStage.FirstLegend, "rawcode:M30h", false) { Round=9 };
        var frame = Frame(plan, ImmutableDictionary<string,int>.Empty.Add("rawcode:120h",1)) with
        { RewardWisps=ImmutableDictionary<string,int>.Empty.Add("e018",wisps) };
        var missing = new RecipeCompletionCalculator(catalog.Unit).Calculate([plan.TargetUnitId!], frame.Inventory).MissingLeaves;
        Assert.Contains(missing, leaf => leaf.MissingCount == 9 &&
            catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains));
        Assert.True(missing.Sum(leaf => leaf.MissingCount) > wisps);
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame, catalog));
        Assert.Null(BulletGuideSelectionPolicy.Decide(frame with { Round = 10 }, catalog));
        Assert.Equal(wisps, frame.RewardWisps["e018"]);
    }

    [Theory]
    [InlineData(21, null)] [InlineData(24, null)]
    [InlineData(24, "AlliedForces.EmergencyCall")]
    public void FixedBountyPlanDoesNotBlockGuideOrRewriteActual(int round, string? actual)
    {
        var inventory = ImmutableDictionary<string,int>.Empty.Add("rawcode:120h",1);
        var plan = new BulletGuidePolicy(catalog).Plan(round, 0, inventory, "악몽",actual);
        var frame=Frame(plan,inventory) with { ConfirmedNavigation=actual };
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.NotEqual(CoachActionKind.Navigation,decision.Kind);
        Assert.Equal(actual,plan.ConfirmedNavigation);
        Assert.Equal(BulletGuidePolicy.NavigationId, plan.PlannedNavigation);
        Assert.Equal(actual, frame.ConfirmedNavigation);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
    }

    [Fact]
    public void NativeAndMainPresentationKeepBountyPlanSeparate_SourceContract()
    {
        var root=Path.GetDirectoryName(SourceFile())!;
        var source=File.ReadAllText(Path.Combine(root,"..","MainWindow.Coach.cs"));
        Assert.DoesNotContain("초반 패가 어려우면 긴급소집이 원문 대안",source);
        Assert.Contains("계획 항법: 바운티헌터 고정",source);
        Assert.Contains("ConfirmedNavigation = EffectiveNavigation",source);
    }
    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path="")=>path;

    [Theory]
    [InlineData(7, "현재 희귀 보유 · 조합 완료 미확인")]
    [InlineData(8, "기한 만료 · 성공 여부 미확인")]
    public void QuestEvidenceDoesNotCallOwnedRareOrExpirySuccess(int round,string expected)
    {
        var plan=new BulletGuidePolicy(catalog).Plan(round,0,
            ImmutableDictionary<string,int>.Empty.Add("rawcode:L50h",1),"악몽");
        var decision=new BeginnerCoachPlanner(catalog).Decide(Frame(plan,
            ImmutableDictionary<string,int>.Empty.Add("rawcode:L50h",1)));
        Assert.NotEqual(BulletGuideStage.FastUniqueRare,plan.Stage);
        Assert.Contains(expected,decision.OperationGuide);
    }

    [Theory]
    [InlineData(FastUniqueState.CompletedVerified, false)]
    [InlineData(FastUniqueState.RarePreviouslyObserved, false)]
    [InlineData(FastUniqueState.TerminalOutcomeUnknown, true)]
    [InlineData(FastUniqueState.Unknown, true)]
    public void ConsumedRareHistoryAndExplicitCompletionDifferFromTerminalFlag(FastUniqueState evidence,bool rareFirst)
    {
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,catalog.Unit("rawcode:L50h").Recipe,"악몽",
            fastUnique:evidence);
        Assert.Equal(rareFirst,plan.Stage==BulletGuideStage.FastUniqueRare);
        Assert.Equal(evidence,plan.FastUnique);
        if (!rareFirst) Assert.Equal("rawcode:M30h",plan.TargetUnitId);
    }

    [Fact]
    public void QuestReadyCraftIsNotBlockedByUnreceivedStoryReward()
    {
        var rare=catalog.Unit("rawcode:L50h");
        var inventory=rare.Recipe.ToImmutableDictionary();
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽");
        var recs=new RecommendationEngine(catalog).RecommendGuideCraft(plan.TargetUnitId!,
            inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray(),plan,7,0);
        var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json")))
            .Plan(recs,inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}));
        var frame=Frame(plan,inventory) with { Recommendations=recs, CraftSteps=steps,
            Story=new(RecommendationSequenceStage.FirstRare,StorySequenceAction.WaitForStoryReward,"","","","","",null,null,0,false) };
        Assert.Equal(CoachActionKind.Craft,new BeginnerCoachPlanner(catalog).Decide(frame).Kind);
    }

    [Fact]
    public void RareObservationHistorySurvivesConsumptionButNotNewSession()
    {
        var rare=ImmutableDictionary<string,int>.Empty.Add("rawcode:L50h",1);
        var policy=new BulletGuidePolicy(catalog);
        var evidence=policy.ObserveFastUnique(FastUniqueState.Unknown,7,rare,true);
        Assert.Equal(FastUniqueState.RarePreviouslyObserved,evidence);
        Assert.Equal(FastUniqueState.Unknown,policy.ObserveFastUnique(FastUniqueState.Unknown,7,rare,false));
        Assert.Equal(FastUniqueState.Unknown,policy.ObserveFastUnique(FastUniqueState.Unknown,8,rare,true));
        var consumed=catalog.Unit("rawcode:L50h").Recipe;
        evidence=policy.ObserveFastUnique(evidence,7,consumed,true);
        Assert.Equal(BulletGuideStage.FirstLegend,policy.Plan(7,0,consumed,"악몽",fastUnique:evidence).Stage);
        Assert.Equal(BulletGuideStage.FastUniqueRare,policy.Plan(7,0,consumed,"악몽",fastUnique:FastUniqueState.Unknown).Stage);
    }

    [Fact]
    public void RareHistoryHasReadyProducerAndSessionReset_SourceContract()
    {
        var root=Path.Combine(Path.GetDirectoryName(SourceFile())!,"..");
        var coach=File.ReadAllText(Path.Combine(root,"MainWindow.Coach.cs"));
        var main=File.ReadAllText(Path.Combine(root,"MainWindow.xaml.cs"));
        Assert.Contains(".ObserveFastUnique(_guideFastUnique",coach);
        Assert.Contains("fastUnique: _guideFastUnique",main);
        Assert.Contains("_guideFastUnique = FastUniqueState.Unknown;",main);
    }

    [Fact]
    public void SelectionUsesJointReservationIncludingDuplicateActiveRoot()
    {
        var inventory=catalog.Unit("rawcode:L50h").Recipe.ToImmutableDictionary();
        var plan=new BulletGuidePlan(BulletGuideStage.FastUniqueRare,"rawcode:L50h",false)
        { Round=7, ProtectedUnitIds=["rawcode:M30h","rawcode:L50h","rawcode:L50h"] };
        var frame=Frame(plan,inventory) with { RewardWisps=ImmutableDictionary<string,int>.Empty.Add("e018",1) };
        var available=BulletGuideReservations.Available(catalog,inventory,plan.ProtectedUnitIds,plan.TargetUnitId);
        var missing=new RecipeCompletionCalculator(catalog.Unit).Calculate([plan.TargetUnitId!],available).MissingLeaves;
        var decision=BulletGuideSelectionPolicy.Decide(frame,catalog);
        var deduplicated=BulletGuideReservations.Available(catalog,inventory,
            ["rawcode:M30h","rawcode:L50h"],plan.TargetUnitId);
        Assert.Equal(available.OrderBy(pair=>pair.Key),deduplicated.OrderBy(pair=>pair.Key));
        Assert.Empty(missing);
        Assert.Null(decision);
        var competing=frame with { GuidePlan=plan with { ProtectedUnitIds=["rawcode:M30h"] } };
        var reserved=BulletGuideReservations.Available(catalog,inventory,competing.GuidePlan!.ProtectedUnitIds,plan.TargetUnitId);
        var deficit=new RecipeCompletionCalculator(catalog.Unit).Calculate([plan.TargetUnitId!],reserved).MissingLeaves;
        Assert.True(deficit.Sum(leaf=>leaf.MissingCount)>frame.RewardWisps["e018"]);
        Assert.Null(BulletGuideSelectionPolicy.Decide(competing,catalog));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void QuestRewardUsesActualRareRootBeforeUnreceivedStoryReward(int wisps)
    {
        var inventory=ImmutableDictionary<string,int>.Empty.Add(catalog.Unit("rawcode:300h").Id,1);
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽");
        var entries=inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray();
        var candidates=RecommendationPipeline.ComputeCandidates(new()
        {
            Mode=PlayMode.Guide,GuidePlan=plan,Engine=new RecommendationEngine(catalog),
            Goal=catalog.Unit(BulletGuidePolicy.GoalId),Inventory=entries,
            InitialSurface=RecommendationSurface.TopAndNavigation,NavigationMode="Unselected",
            Gorosei=GoroseiMode.None,BuildVariant=BuildVariants.AutoId,Difficulty="악몽",Round=7,
            StorySequence=new(RecommendationSequenceStage.FirstRare,StorySequenceAction.WaitForStoryReward,"","","","","",null,null,0,false)
        });
        var final=RecommendationPipeline.Finalize(candidates,catalog,catalog.Unit(BulletGuidePolicy.GoalId),entries,new FirstRareTargetPolicy(),7,0,"악몽");
        var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json"))).Plan(final.Recommendations,entries);
        Assert.Empty(steps);
        Assert.Equal(BulletGuideStage.FastUniqueRare,plan.Stage);
        var frame=Frame(plan,inventory) with { Recommendations=final.Recommendations,CraftSteps=steps,Story=final.StorySequence,
            RewardWisps=ImmutableDictionary<string,int>.Empty.Add("e018",wisps) };
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame);
        var deficit=new RecipeCompletionCalculator(catalog.Unit).Calculate([plan.TargetUnitId!],inventory).MissingLeaves;
        Assert.True(deficit.Sum(leaf=>leaf.MissingCount)>wisps);
        Assert.Equal(CoachActionKind.Gather,decision.Kind);
        Assert.Null(decision.RewardWispId);
        Assert.Null(decision.SelectionBatch);
        Assert.Equal(plan.TargetUnitId,decision.TargetUnitId);
        Assert.Equal("희귀함",catalog.Unit(plan.TargetUnitId!).Tier);
        Assert.Equal(BulletGuideAdvice.Stage(plan),decision.Milestone);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)]
    public void RarePlanBudgetsOnlyObservedWispsAndReportsShortfall(int wisps)
    {
        var inventory=ImmutableDictionary<string,int>.Empty.Add(catalog.Unit("rawcode:300h").Id,1);
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽",selectionWisps:wisps);
        var missing=new RecipeCompletionCalculator(catalog.Unit).Calculate([plan.TargetUnitId!],inventory).MissingLeaves;
        var common=missing.Where(leaf=>catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains))
            .Sum(leaf=>leaf.MissingCount);
        Assert.Equal(wisps,plan.SelectionWisps);
        Assert.Equal(common,plan.RareCommonDeficit);
        Assert.Equal(missing.Sum(leaf=>leaf.MissingCount)-common,plan.RareOtherDeficit);
        Assert.True(plan.RareOtherDeficit+Math.Max(0,plan.RareCommonDeficit-plan.SelectionWisps)>0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SufficientMinimumQuestBatchPrecedesUnreceivedStoryReward(int wisps)
    {
        const string target="rawcode:L50h";
        var leaves=new RecipeCompletionCalculator(catalog.Unit).Calculate([target],ImmutableDictionary<string,int>.Empty).Leaves;
        var hand=leaves.ToImmutableDictionary(leaf=>leaf.UnitId,leaf=>checked((int)leaf.RequiredCount));
        var common=leaves.First(leaf=>catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains)).UnitId;
        var plan=new BulletGuidePlan(BulletGuideStage.FastUniqueRare,target,false){Round=7};
        var frame=Frame(plan,hand.SetItem(common,hand[common]-1)) with
        {
            RewardWisps=ImmutableDictionary<string,int>.Empty.Add("e018",wisps),
            Signals=ImmutableDictionary<string,long?>.Empty.Add("lumber",100),
            Story=new(RecommendationSequenceStage.FirstRare,StorySequenceAction.WaitForStoryReward,"","","","","",null,null,0,false)
        };
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(CoachActionKind.Reward,decision.Kind);
        Assert.Equal("e018",decision.RewardWispId);
        Assert.Equal(target,decision.SelectionBatch!.TargetUnitId);
        Assert.Equal(new SelectionWispItem(common,1),Assert.Single(decision.SelectionBatch.Items));
        Assert.Equal(wisps-1,decision.SelectionWispsAfterBatch);
        Assert.Equal(wisps,frame.RewardWisps["e018"]);
    }

    [Fact]
    public void PriorHiddenCommitmentCannotStarveQuestRareInFinalCraft()
    {
        var inventory=catalog.Unit("rawcode:L50h").Recipe.ToImmutableDictionary();
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽");
        var entries=inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray();
        var recs=new RecommendationEngine(catalog).RecommendGuideCraft(plan.TargetUnitId!,entries,plan,7,0);
        var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json"))).Plan(recs,entries);
        Assert.NotEmpty(steps);
        var frame=Frame(plan,inventory) with { Recommendations=recs,CraftSteps=steps,CommittedCraftUnitId="rawcode:M30h" };
        Assert.Equal(CoachActionKind.Craft,new BeginnerCoachPlanner(catalog).Decide(frame).Kind);
    }

    [Fact]
    public void ObservedWispBudgetIsWiredFromMain_SourceContract()
    {
        var source=File.ReadAllText(Path.Combine(Path.GetDirectoryName(SourceFile())!,"..","MainWindow.xaml.cs"));
        Assert.Contains("selectionWisps: _coachCurrent ? _mapSignals.RewardWisps.GetValueOrDefault(\"e018\") : 0",source);
    }

    [Fact]
    public void CatalogRecipeRaresExactlyMatchPinnedMapA0B9Roster_SourceContract()
    {
        // war3map.w3u SHA a9aa2cb9c08130c3bee970aecb05b62fdc3db867685f4997dd2533735d2ea278.
        // Bounded v3 parser consumed all 857425 bytes; uabi contains A0B9 for exactly these native IDs.
        string[] native=["h01L","h01M","h01N","h01O","h01P","h01Q","h01R","h01S","h01T","h01U","h01V","h01W","h01X","h01Y","h01Z",
            "h020","h021","h022","h023","h024","h025","h026","h027","h028","h029","h02A","h02B","h02C","h02D","h02E","h02F","h02G","h02H","h02I","h02J","h02K","h02L","h02M","h04H","h05K","h05L","h09X"];
        var expected=native.Select(code=>catalog.Unit("rawcode:"+new string(code.Reverse().ToArray())).Id).Distinct().Order().ToArray();
        var actual=catalog.AllUnits.Where(unit=>TopGradePolicy.BaseTier(unit.Tier)=="희귀함" && unit.Recipe.Count>0).Select(unit=>unit.Id).Distinct().Order().ToArray();
        Assert.Equal(expected,actual);
        Assert.Contains(catalog.Unit("rawcode:L50h").Id,actual);
        Assert.DoesNotContain(catalog.Unit("rawcode:M30h").Id,actual);
        // Perona has a zombie leaf: e018 cannot fill every rare recipe leaf.
        Assert.Contains(new RecipeCompletionCalculator(catalog.Unit).Calculate(["rawcode:K50h"],ImmutableDictionary<string,int>.Empty).MissingLeaves,
            leaf=>leaf.UnitId=="rawcode:H00h" && !catalog.Unit(leaf.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains));
    }

    [Theory]
    [InlineData("paused")] [InlineData("stale")] [InlineData("clear")] [InlineData("fail")]
    [InlineData("line")] [InlineData("conflict")]
    public void QuestDoesNotBypassFinalSafetyGuards(string guard)
    {
        var inventory=catalog.Unit("rawcode:L50h").Recipe.ToImmutableDictionary();
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽");
        var entries=inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray();
        var recs=new RecommendationEngine(catalog).RecommendGuideCraft(plan.TargetUnitId!,entries,plan,7,0);
        var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json"))).Plan(recs,entries);
        var frame=Frame(plan,inventory) with { Recommendations=recs,CraftSteps=steps,
            RewardWisps=ImmutableDictionary<string,int>.Empty.Add("e018",3),
            Paused=guard=="paused",IsCurrent=guard!="stale",Outcome=guard is "clear" or "fail" ? guard : "",
            NativeNavigation=guard=="conflict" ? new(NativeNavigationStatus.Conflict,null,"synthetic conflict") : NativeNavigationSnapshot.Unknown,
            Signals=guard=="line" ? ImmutableDictionary<string,long?>.Empty.Add("line-count",70) : ImmutableDictionary<string,long?>.Empty };
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.NotEqual(CoachActionKind.Craft,decision.Kind);
        Assert.NotEqual(CoachActionKind.Reward,decision.Kind);
    }

    [Theory]
    [InlineData(PlayMode.Beginner)] [InlineData(PlayMode.Manual)]
    public void OtherModesStillAskForActualNavigation(PlayMode mode)
    {
        var plan=new BulletGuidePlan(BulletGuideStage.FirstLegend,"rawcode:M30h",false){Round=24};
        var frame=Frame(plan,ImmutableDictionary<string,int>.Empty.Add("rawcode:120h",1)) with { Mode=mode,GuidePlan=null };
        Assert.Equal(CoachActionKind.Navigation,new BeginnerCoachPlanner(catalog).Decide(frame).Kind);
    }

    [Theory]
    [InlineData("M30h")] [InlineData("180h")]
    public void HigherOwnedBodyIsNotProofOfT000RareCraft(string code)
    {
        var inventory=catalog.Unit("rawcode:L50h").Recipe.ToImmutableDictionary().Add("rawcode:"+code,1);
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽");
        Assert.Equal(FastUniqueState.Unknown,plan.FastUnique);
        Assert.Equal(BulletGuideStage.FastUniqueRare,plan.Stage);
    }

    [Fact]
    public void EquallyReadyRaresPreferActualBulletRecipeFitAfterCompletionCost()
    {
        var inventory=catalog.Unit("rawcode:020h").Recipe.Concat(catalog.Unit("rawcode:120h").Recipe)
            .GroupBy(p=>p.Key).ToImmutableDictionary(g=>g.Key,g=>g.Sum(p=>p.Value));
        Assert.True(BulletGuideCraftSafety.Allows(catalog,"rawcode:120h",inventory,7,null));
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽");
        Assert.Equal(BulletGuideStage.FastUniqueRare,plan.Stage);
        Assert.True(BulletGuideReservations.IsRecipeStep(catalog,BulletGuidePolicy.GoalId,plan.TargetUnitId!),plan.TargetUnitId);
    }

    [Fact]
    public void WispBudgetCannotPretendZombieDeficitIsSelectableCommon()
    {
        var inventory=catalog.Unit("rawcode:K50h").Recipe.Where(p=>p.Key!="rawcode:010h")
            .ToImmutableDictionary().Add(catalog.Unit("rawcode:100h").Id,1);
        var plan=new BulletGuidePolicy(catalog).Plan(7,0,inventory,"악몽",selectionWisps:3);
        long Residual(string id)
        {
            var missing=new RecipeCompletionCalculator(catalog.Unit).Calculate([id],inventory).MissingLeaves;
            var common=missing.Where(l=>catalog.Unit(l.UnitId).Rawcodes.Any(BulletGuideSupportPolicy.CommonCodes.Contains)).Sum(l=>l.MissingCount);
            return missing.Sum(l=>l.MissingCount)-Math.Min(3,common);
        }
        var min=catalog.AllUnits.Where(u=>TopGradePolicy.BaseTier(u.Tier)=="희귀함" && u.Recipe.Count>0)
            .Where(u=>u.Recipe.Any(p=>inventory.GetValueOrDefault(p.Key)<p.Value) || BulletGuideCraftSafety.Allows(catalog,u.Id,inventory,7,null))
            .Min(u=>Residual(u.Id));
        Assert.Equal(min,Residual(plan.TargetUnitId!));
    }

    [Theory]
    [InlineData("PathOfKings.MartialLaw", "계엄령")]
    [InlineData(null, "미확인")]
    public void ObservedNavigationKeepsFixedBountyPlanButRejectsStaleTopCraft(string? actualId,string label)
    {
        var inventory=catalog.Unit(BulletGuidePolicy.GoalId).Recipe.ToImmutableDictionary(p=>p.Key,p=>p.Value*2)
            .Add(catalog.Unit("rawcode:300h").Id,20);
        var plan=new BulletGuidePlan(BulletGuideStage.CraftBullet,BulletGuidePolicy.GoalId,false)
            {Round=50,ConfirmedNavigation=BulletGuidePolicy.NavigationId};
        var entries=inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray();
        var recs=new RecommendationEngine(catalog).RecommendGuideCraft(plan.TargetUnitId!,entries,plan,50,13);
        var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json"))).Plan(recs,entries);
        Assert.NotEmpty(steps);
        var frame=Frame(plan,inventory) with { Recommendations=recs,CraftSteps=steps,CompletedStoryStage=13,
            Signals=ImmutableDictionary<string,long?>.Empty.Add("lumber",100),
            NativeNavigation=new(NativeNavigationStatus.Selected,BulletGuidePolicy.NavigationId,"synthetic current selection") };
        Assert.Equal(CoachActionKind.Craft,new BeginnerCoachPlanner(catalog).Decide(frame).Kind);
        var actual=actualId is null ? NativeNavigationSnapshot.Unknown : new NativeNavigationSnapshot(NativeNavigationStatus.Selected,actualId,"synthetic new actual selection");
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame with { NativeNavigation=actual });
        Assert.NotEqual(CoachActionKind.Craft,decision.Kind);
        if (actualId is not null) Assert.Contains(label,decision.Reason);
        Assert.Equal("guide1:actual-navigation-limit:" + BulletGuidePolicy.GoalId, decision.Id);
        Assert.Equal(actualId, actual.Resolve(frame.ConfirmedNavigation));
        Assert.Equal(BulletGuideAdvice.Operation(frame with { NativeNavigation = actual,
            ConfirmedNavigation = actual.Resolve(frame.ConfirmedNavigation) }), decision.OperationGuide);
        Assert.Equal(BulletGuidePolicy.NavigationId,plan.PlannedNavigation);
    }

    [Theory]
    [InlineData("PathOfKings.BountyHunter")]
    [InlineData("PathOfKings.RoyalLoader")]
    public void Fur02ExistingBulletDoesNotPayForStaleAdditionalCraft(string actualId)
    {
        var inventory=catalog.Unit(BulletGuidePolicy.GoalId).Recipe.ToImmutableDictionary(p=>p.Key,p=>p.Value*2)
            .Add(catalog.Unit("rawcode:300h").Id,20);
        var plan=new BulletGuidePlan(BulletGuideStage.CraftBullet,BulletGuidePolicy.GoalId,false)
            {Round=50,ConfirmedNavigation=BulletGuidePolicy.NavigationId};
        var entries=inventory.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray();
        var recs=new RecommendationEngine(catalog).RecommendGuideCraft(plan.TargetUnitId!,entries,plan,50,13);
        var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json"))).Plan(recs,entries);
        Assert.Contains(steps,s=>s.TargetUnitId==BulletGuidePolicy.GoalId);
        var frame=Frame(plan,inventory) with { Recommendations=recs,CraftSteps=steps,CompletedStoryStage=13,
            Signals=ImmutableDictionary<string,long?>.Empty.Add("lumber",100),
            NativeNavigation=new(NativeNavigationStatus.Selected,actualId,"synthetic current actual") };
        var planner=new BeginnerCoachPlanner(catalog);
        Assert.Equal(CoachActionKind.Craft,planner.Decide(frame).Kind);
        // Keep the actual upstream-produced step, but reobserve an already owned result.
        var current=frame with {Inventory=inventory.Add(BulletGuidePolicy.GoalId,1)};
        var decision=planner.Decide(current);
        Assert.Equal(CoachActionKind.Waiting,decision.Kind);
        Assert.Contains("actual-navigation-limit",decision.Id);
        Assert.Contains("조합 후 2기",decision.Reason);
        Assert.Equal(BulletGuidePolicy.NavigationId,plan.PlannedNavigation);
        Assert.Equal(actualId,current.NativeNavigation.OptionId);
        Assert.Equal(CoachActionKind.Craft,planner.Decide(current with {
            NativeNavigation=new(NativeNavigationStatus.Selected,"AlliedForces.EmergencyCall","synthetic unlimited actual") }).Kind);
        Assert.NotEqual(CoachActionKind.Craft,planner.Decide(frame with {Signals=ImmutableDictionary<string,long?>.Empty}).Kind);
        var ingredient=catalog.Unit(BulletGuidePolicy.GoalId).Recipe.First(p=>catalog.Unit(p.Key).Tier!="자원").Key;
        Assert.Contains("reservation",planner.Decide(current with {Inventory=current.Inventory.Remove(ingredient)}).Id);
        // Protected root bodies and a changed goal still invalidate the old real action.
        Assert.Contains("reservation",planner.Decide(frame with {Inventory=inventory.SetItem(ingredient,1),SelectedGoalIds=[ingredient]}).Id);
        Assert.Contains("reservation",planner.Decide(frame with {GuidePlan=plan with {TargetUnitId="rawcode:L50h"}}).Id);
    }

    [Fact]
    public void Fur02DirectProjectionConsumesIngredientsNotOwnedResultAndRejectsPartialRecipe()
    {
        var targets=catalog.AllUnits.Where(u=>u.Recipe.Count>0).ToArray();
        Assert.NotEmpty(targets);
        foreach(var target in targets)
        {
            var inventory=target.Recipe.Where(p=>catalog.Unit(p.Key).Tier!="자원")
                .ToImmutableDictionary(p=>p.Key,p=>p.Value*2);
            inventory=inventory.SetItem(target.Id,inventory.GetValueOrDefault(target.Id)+1);
            var before=inventory;
            var after=BulletGuideCraftSafety.ProjectAfterCraft(catalog,target.Id,inventory);
            Assert.NotNull(after);
            foreach(var pair in inventory)
            {
                var spent=target.Recipe.GetValueOrDefault(pair.Key);
                Assert.Equal(pair.Value-spent+(pair.Key==target.Id ? 1 : 0),after[pair.Key]);
            }
            Assert.Equal(before,inventory);
            foreach(var ingredient in target.Recipe.Where(p=>catalog.Unit(p.Key).Tier!="자원"))
                Assert.Null(BulletGuideCraftSafety.ProjectAfterCraft(catalog,target.Id,inventory.SetItem(ingredient.Key,ingredient.Value-1)));
        }
    }

    [Theory]
    [InlineData("e016")] [InlineData("e017")] [InlineData("e019")]
    public void Fur01ObservedRewardSurvivesQuestPriorityAndReobservesConsumption(string reward)
    {
        var inventory=ImmutableDictionary<string,int>.Empty.Add(catalog.Unit("rawcode:300h").Id,1);
        CoachFrame Observe(ImmutableDictionary<string,int> hand, int count)
        {
            var plan=new BulletGuidePolicy(catalog).Plan(7,0,hand,"악몽",selectionWisps:0);
            var entries=hand.Select(p=>new InventoryEntry{UnitId=p.Key,Count=p.Value}).ToArray();
            var candidates=RecommendationPipeline.ComputeCandidates(new()
            {
                Mode=PlayMode.Guide,GuidePlan=plan,Engine=new RecommendationEngine(catalog),
                Goal=catalog.Unit(BulletGuidePolicy.GoalId),Inventory=entries,
                InitialSurface=RecommendationSurface.TopAndNavigation,NavigationMode="Unselected",
                Gorosei=GoroseiMode.None,BuildVariant=BuildVariants.AutoId,Difficulty="악몽",Round=7
            });
            var steps=new AutoCombinePlanner(catalog,CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Data","tmo-combine-hotkeys.json"))).Plan(candidates.Recommendations,entries);
            return Frame(plan,hand) with { Recommendations=candidates.Recommendations.ToImmutableArray(),CraftSteps=steps,
                Story=new(RecommendationSequenceStage.FirstRare,reward=="e019" ? StorySequenceAction.SpendRareWisps : StorySequenceAction.SpendStoryWisps,"","","","","",null,null,0,false),
                RewardWisps=ImmutableDictionary<string,int>.Empty.Add("e018",0).Add(reward,count) };
        }
        var planner=new BeginnerCoachPlanner(catalog);
        for(var frame=0;frame<2;frame++) Assert.NotEqual(CoachActionKind.Reward,planner.Decide(Observe(inventory,0)).Kind);
        for(var frame=0;frame<2;frame++)
        {
            var received=planner.Decide(Observe(inventory,1));
            Assert.Equal(CoachActionKind.Reward,received.Kind);
            Assert.Equal(reward,received.RewardWispId);
            Assert.Contains("한 번",received.Controls);
        }
        Assert.NotEqual(CoachActionKind.Reward,planner.Decide(Observe(inventory,0)).Kind);
        var ready=catalog.Unit("rawcode:L50h").Recipe.ToImmutableDictionary();
        var readyFrame=Observe(ready,1);
        Assert.NotEmpty(readyFrame.CraftSteps);
        Assert.Equal(CoachActionKind.Craft,planner.Decide(readyFrame).Kind);
        var next=Observe(ImmutableDictionary<string,int>.Empty.Add(catalog.Unit("rawcode:L50h").Id,1),0);
        Assert.NotEqual(BulletGuideStage.FastUniqueRare,next.GuidePlan!.Stage);
        Assert.NotEqual(CoachActionKind.Reward,planner.Decide(next).Kind);
    }

    private static CoachFrame Frame(BulletGuidePlan plan, ImmutableDictionary<string,int> inventory) => new()
    {
        Mode=PlayMode.Guide, GuideNumber=1, GuidePlan=plan, Round=plan.Round,
        MatchGeneration=0, Revision=1, RecognitionRevision=1, CompletedStoryStage=0,
        IsCurrent=true, Difficulty="악몽", Inventory=inventory
    };

    [Fact]
    public void ReadyRareBeatsOpeningHiddenThroughRealPipelineAndFinalAction()
    {
        var rare = catalog.Unit("rawcode:L50h");
        var inventory = rare.Recipe.ToImmutableDictionary();
        var plan = new BulletGuidePolicy(catalog).Plan(7, 0, inventory, "악몽");
        Assert.Equal("희귀함", catalog.Unit(plan.TargetUnitId!).Tier);
        var entries = inventory.Select(p => new InventoryEntry { UnitId=p.Key, Count=p.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new()
        {
            Mode=PlayMode.Guide, GuidePlan=plan, Engine=new RecommendationEngine(catalog),
            Goal=catalog.Unit(BulletGuidePolicy.GoalId), Inventory=entries,
            InitialSurface=RecommendationSurface.TopAndNavigation, NavigationMode="Unselected",
            Gorosei=GoroseiMode.None, BuildVariant=BuildVariants.AutoId, Difficulty="악몽", Round=7
        });
        Assert.Equal(plan.TargetUnitId, Assert.Single(candidates.Recommendations).Route.GoalUnitId);
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries);
        Assert.NotEmpty(steps);
        var decision = new BeginnerCoachPlanner(catalog).Decide(new CoachFrame
        {
            Mode=PlayMode.Guide, GuideNumber=1, GuidePlan=plan, Round=7, IsCurrent=true,
            Difficulty="악몽", MatchGeneration=0, CompletedStoryStage=0, Inventory=inventory, Recommendations=candidates.Recommendations.ToImmutableArray(),
            CraftSteps=steps.ToImmutableArray(), Revision=1, RecognitionRevision=1
        });
        Assert.Equal(CoachActionKind.Craft, decision.Kind);
        Assert.Equal(plan.TargetUnitId, decision.TargetUnitId);
        Assert.Contains("희귀함", decision.Title);
    }
}
