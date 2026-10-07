using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletFifthCompletionTests
{
    private readonly DataCatalog catalog = BulletGuideUncommonSaleTests.Catalog();
    private static Dictionary<string,int> Counts(params string[] codes) => codes.GroupBy(c => "rawcode:" + c).ToDictionary(g => g.Key, g => g.Count());
    private static Dictionary<string,int> Ready() => Counts("180h", "U30h", "540h", "M30h", "H30h", "N30h", "O30h", "Y30h", "Q30h", "K50h");
    private (BulletGuidePlan Plan, CoachFrame Frame, CoachDecision Decision) Run(Dictionary<string,int> inventory, int round = 50, int story = 13, GoroseiMode mode = GoroseiMode.None, bool? race = null, string? target = null, string[]? roots = null)
    {
        var plan = new BulletGuidePolicy(catalog).Plan(round, story, inventory, "악몽", BulletGuidePolicy.NavigationId, mode, destructionKingAvailable: race);
        if (target is not null) plan = plan with { TargetUnitId = target };
        if (roots is not null) plan = plan with { ProtectedUnitIds = plan.ProtectedUnitIds.Concat(roots).Distinct().ToArray() };
        var hotkeys = CombineHotkeyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "tmo-combine-hotkeys.json"));
        var entries = inventory.Select(p => new InventoryEntry { UnitId = p.Key, Count = p.Value }).ToArray();
        var candidates = RecommendationPipeline.ComputeCandidates(new RecommendationPipelineRequest {
            Mode = PlayMode.Guide, GuidePlan = plan, Engine = new RecommendationEngine(catalog, combineHotkeys: hotkeys),
            Goal = catalog.Unit(BulletGuidePolicy.GoalId), Inventory = entries, InitialSurface = RecommendationSurface.TopAndNavigation,
            NavigationMode = BulletGuidePolicy.NavigationId, Gorosei = mode, BuildVariant = BuildVariants.AutoId, Difficulty = "악몽", Round = round, CompletedStoryStage = story });
        var steps = new AutoCombinePlanner(catalog, hotkeys).Plan(candidates.Recommendations, entries, protectedUnitIds: plan.ProtectedUnitIds);
        var frame = new CoachFrame { Mode = PlayMode.Guide, GuideNumber = 1, Round = round, CompletedStoryStage = story,
            IsCurrent = true, Difficulty = "악몽", Inventory = inventory.ToImmutableDictionary(), GuidePlan = plan,
            MatchGeneration = 1, Revision = 1, ConfirmedNavigation = BulletGuidePolicy.NavigationId,
            CraftSteps = steps, Recommendations = candidates.Recommendations, Signals = ImmutableDictionary<string,long?>.Empty.Add("lumber",100) };
        return (plan, frame, new BeginnerCoachPlanner(catalog).Decide(frame));
    }

    [Theory]
    [InlineData(59)]
    [InlineData(60)]
    public void GuideAdviceReachesPipelineWithoutTurningReferenceThresholdsIntoActions(int round)
    {
        var result = Run(Ready(), round);
        Assert.Equal(BulletGuideAdvice.Operation(result.Frame), result.Decision.OperationGuide);
        Assert.NotEqual(CoachActionKind.Upgrade, result.Decision.Kind);
        Assert.Null(result.Frame.GuideRuntime.BulletArmorReduction);
        Assert.Equal(BulletTraitState.Unknown, result.Frame.GuideRuntime.Trait(BulletUpgradeAbility.Armor));
        Assert.DoesNotContain("600k", result.Decision.OperationGuide);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(40, true)]
    public void CharacterizationOptionalChopperBoundary(int commons, bool expected)
    {
        var inventory = Ready(); inventory["luffy_common"] = commons;
        var result = Run(inventory);
        Assert.Equal(expected, result.Plan.TargetUnitId == "rawcode:K20h");
        Assert.NotEqual(CoachActionKind.Craft, result.Decision.Kind); // Luffy alone cannot supply recipe.
    }

    // Source roster table: all six named specials; values are nominal, never active effect credit.
    public static TheoryData<string,string,string,int,int> SpecialRows => new() {
        {"E10h","720h","B02G",3,0}, {"610h","U10h","B011",3,0}, {"Y00h","R10h","B004",0,5},
        {"A10h","P10h","B001",0,5}, {"F10h","Y10h","B012",0,5}, {"D10h","L10h","attack-role",0,0} };
    [Theory]
    [MemberData(nameof(SpecialRows))]
    public void NamedAuxiliarySpecialMayBeSpentByActualConsumer(string retained, string target, string group, int armor, int slow)
    {
        var nominal=Run(Counts("180h",retained,retained));
        Assert.Equal(armor,nominal.Plan.Support!.ArmorPotential);
        Assert.Equal(slow+7,nominal.Plan.Support.SlowPotential); // copies do not sum; navigation separate
        Assert.Contains(retained,BulletGuidePolicy.RetainedCodes);
        var inventory = catalog.Unit("rawcode:"+target).Recipe.ToDictionary(p=>p.Key,p=>p.Value);
        inventory["rawcode:180h"] = 1;
        Assert.Equal(1,inventory["rawcode:"+retained]);
        Assert.NotEmpty(group);
        // Before the independent round-50 common reserve becomes mandatory.
        var result = Run(inventory, round:40, target:"rawcode:"+target);
        Assert.Equal(CoachActionKind.Craft,result.Decision.Kind);
        Assert.Equal("rawcode:"+target,result.Decision.TargetUnitId);
    }

    [Theory]
    [InlineData("X90h","Z90h",1,true)]
    [InlineData("X90h","Z90h",2,true)]
    [InlineData("B20h","I30h",1,true)]
    [InlineData("B20h","I30h",2,true)]
    [InlineData("B20h","630h",1,true)]
    [InlineData("X90h","HA0h",1,true)]
    public void AuxiliaryRarePromotionDoesNotRequireSpareOrNamedException(string rare,string target,int count,bool allow)
    {
        var inventory = catalog.Unit("rawcode:"+target).Recipe.ToDictionary(p=>p.Key,p=>p.Value);
        inventory["rawcode:180h"] = 1;
        // Keep the other recipe roles intact to isolate this rare's preservation contract.
        foreach (var id in inventory.Keys.Where(id => id != "rawcode:"+rare && catalog.Unit(id).Tier != "자원").ToArray()) inventory[id] *= 2;
        inventory["rawcode:"+rare]=count;
        var result = Run(inventory,round:40,target:"rawcode:"+target);
        Assert.True(allow == (result.Decision.Kind==CoachActionKind.Craft), $"expected={allow}; {result.Decision.Kind}: {result.Decision.Controls}; safety={BulletGuideCraftSafety.Allows(catalog, "rawcode:"+target, inventory, 40, BulletGuidePolicy.NavigationId)}");
    }

    [Theory]
    [InlineData("selected",1,false)]
    [InlineData("committed",1,false)]
    [InlineData("support",1,false)]
    [InlineData("selected",2,true)]
    [InlineData("committed",2,true)]
    public void OptionalChopperRespectsOtherRootAtFinalConsumer(string source,int shared,bool craft)
    {
        var inventory = Ready(); inventory["luffy_common"]=30;
        foreach(var p in catalog.Unit("rawcode:K20h").Recipe) inventory[p.Key]=p.Value;
        inventory["rawcode:D10h"]=shared;
        var run = Run(inventory,roots: source=="support" ? ["rawcode:L10h"] : null);
        Assert.Equal("rawcode:K20h",run.Plan.TargetUnitId);
        var frame = run.Frame with {
            SelectedGoalIds = source=="selected" ? ["rawcode:L10h"] : [],
            CommittedCraftUnitId = source=="committed" ? "rawcode:L10h" : null };
        var result = new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.True(craft==(result.Kind==CoachActionKind.Craft),$"{source} {shared}: {result.Kind} {result.Controls}");
    }

    [Theory]
    [InlineData(11,3,false)] [InlineData(11,12,false)]
    [InlineData(12,3,true)] [InlineData(12,12,true)]
    public void CharacterizationG05RoundDeadlineNotStory(int round,int story,bool overdue)
    {
        var result=Run(Counts(),round,story);
        Assert.Equal(BulletGuideStage.FirstLegend,result.Plan.Stage);
        Assert.Equal(overdue,result.Decision.Milestone.Contains("12라 목표 경과"));
        Assert.NotEqual(CoachActionKind.Finished,result.Decision.Kind);
    }

    [Theory]
    [InlineData("H30h,S30h", "MC0h", 0)]
    [InlineData("HA0h,MC0h", "930h", 1)]
    [InlineData("930h,MC0h", "U30h", 1)]
    [InlineData("830h,MC0h", "930h", 0)]
    [InlineData("HA0h,HA0h", "U20h", 2)]
    public void CharacterizationG06To08CurrentRosterNotAcquisitionOrder(string roster,string target,int air)
    {
        var result=Run(Counts(roster.Split(',')),17,9);
        Assert.Equal("rawcode:"+target,result.Plan.TargetUnitId);
        Assert.Equal(air,result.Plan.AirCount);
        Assert.Equal(BulletGuideAdvice.Operation(result.Frame), result.Decision.OperationGuide);
        Assert.Contains("공방 여부 미확인",result.Decision.OperationGuide);
        Assert.NotEqual(CoachActionKind.Craft,result.Decision.Kind);
    }

    [Theory]
    [InlineData(null,29,10,false)] [InlineData(false,29,10,false)]
    [InlineData(true,29,10,true)] [InlineData(true,30,10,false)] [InlineData(true,29,11,false)]
    public void CharacterizationG21ObservedMissionExpiryAndFourthRecipe(bool? active,int round,int story,bool race)
    {
        var inventory=Counts("930h","V20h","U30h");
        foreach(var p in catalog.Unit("rawcode:U20h").Recipe) inventory[p.Key]=p.Value;
        var result=Run(inventory,round,story,race:active);
        Assert.Equal(race,result.Plan.PursueDestructionKing);
        if(race) { Assert.Equal(BulletGuideStage.DestructionRace,result.Plan.Stage); Assert.Equal("rawcode:U20h",result.Decision.TargetUnitId); Assert.Equal(CoachActionKind.Craft,result.Decision.Kind); }
        Assert.DoesNotContain("위습 지급 완료",result.Decision.OperationGuide);
    }

    [Theory]
    [InlineData("IC0h,O30h",true)] [InlineData("IC0h,Y30h",true)]
    [InlineData("Z20h",true)] [InlineData("930h,930h",true)]
    [InlineData("W20h,O30h",true)] [InlineData("W20h,Y30h",true)]
    [InlineData("O30h,Y30h",true)] [InlineData("IC0h,B30h",false)] [InlineData("930h",false)]
    public void CharacterizationG26To27SurvivingSourceStunRoster(string roster,bool ready)
    {
        var inventory=Counts(roster.Split(','));
        var result=Run(inventory,40,13);
        Assert.Equal(ready,result.Plan.Support!.StunPairReady);
        Assert.Contains("능력·대상·쿨다운 미확인",result.Decision.OperationGuide);
        Assert.DoesNotContain("공폭 사용",result.Decision.Controls);
    }

    [Theory]
    [InlineData("Y00h","D20h","R10h",15)]
    [InlineData("A10h","H20h","P10h",15)]
    [InlineData("F10h","V20h","Y10h",50)]
    public void CharacterizationSameBuffMaxAllowsUnreservedPromotion(string special,string stronger,string target,int slow)
    {
        var inventory=catalog.Unit("rawcode:"+target).Recipe.ToDictionary(p=>p.Key,p=>p.Value);
        inventory["rawcode:180h"]=1; inventory["rawcode:"+stronger]=2;
        var result=Run(inventory,40,target:"rawcode:"+target);
        Assert.Equal(slow+7,result.Plan.Support!.SlowPotential);
        Assert.Equal(CoachActionKind.Craft,result.Decision.Kind);
        Assert.Equal("rawcode:"+target,result.Decision.TargetUnitId);
        var reserved=Run(inventory,40,target:"rawcode:"+target,roots:["rawcode:"+special]);
        Assert.NotEqual(CoachActionKind.Craft,reserved.Decision.Kind);
    }

    [Theory]
    [InlineData(50,0)] [InlineData(60,0)] [InlineData(60,100000)] [InlineData(65,100000)]
    public void ExcludedInvestmentContentCannotChangeAction(int round,int gold)
    {
        var result=Run(Ready(),round);
        var frame=result.Frame with { Signals=result.Frame.Signals.SetItem("gold",gold),
            GuideRuntime=new BulletGuideRuntimeState(true,3,3,3,"fixture").WithExactCounts(new(30,30,30)) };
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(result.Decision.Kind,decision.Kind);
        Assert.Equal(result.Decision.TargetUnitId,decision.TargetUnitId);
        Assert.Equal(result.Decision.Controls,decision.Controls);
        Assert.Equal(result.Decision.Id, decision.Id);
        Assert.Equal(BulletGuideAdvice.Operation(frame), decision.OperationGuide);
        Assert.DoesNotContain("독약",decision.Controls);
        Assert.DoesNotContain("해루",decision.Controls);
    }

    [Theory]
    [InlineData(GoroseiMode.None)] [InlineData(GoroseiMode.Saturn)]
    public void G48G51UnknownNeedIsExplanationNotInventedAlternativeTarget(GoroseiMode mode)
    {
        var result=Run(Ready(),mode:mode);
        Assert.NotEqual("rawcode:140h",result.Plan.TargetUnitId);
        Assert.NotEqual("rawcode:W20h",result.Plan.TargetUnitId);
        Assert.Contains("누수·필요 조건 미확인",result.Decision.OperationGuide);
        if(mode==GoroseiMode.Saturn) Assert.Equal("rawcode:K20h",result.Plan.TargetUnitId);
    }

    [Theory]
    [InlineData(29,"selected",false)] [InlineData(30,"selected",true)] [InlineData(40,"selected",true)]
    [InlineData(30,"committed",true)] [InlineData(30,"support",true)]
    public void OptionalChopperIntermediateCannotSpendJointCommon(int commons,string source,bool optional)
    {
        var inventory=Ready();
        foreach(var p in catalog.Unit("rawcode:K20h").Recipe) inventory[p.Key]=p.Value;
        inventory.Remove("rawcode:D10h");
        foreach(var p in catalog.Unit("rawcode:D10h").Recipe) inventory[p.Key]=p.Value;
        var common=catalog.Unit("rawcode:D10h").Recipe.Keys.Single(id=>catalog.Unit(id).Tier=="흔함");
        inventory["luffy_common"]=commons-inventory[common];
        var run=Run(inventory);
        Assert.Equal(optional,run.Plan.TargetUnitId=="rawcode:K20h");
        if(optional) { Assert.Equal(CoachActionKind.Craft,run.Decision.Kind); Assert.Equal("rawcode:D10h",run.Decision.TargetUnitId); }
        // Competing Smoker-special recipe reserves the same sword-soldier common.
        // A common itself is not a committed craft; use a real other recipe root.
        const string otherRoot="rawcode:F10h";
        Assert.True(catalog.Unit(otherRoot).Recipe.ContainsKey(common));
        var frame=run.Frame with {
            SelectedGoalIds=source=="selected" ? [otherRoot] : [],
            CommittedCraftUnitId=source=="committed" ? otherRoot : null,
            GuidePlan=source=="support" ? run.Plan with { ProtectedUnitIds=run.Plan.ProtectedUnitIds.Concat(new[]{otherRoot}).ToArray() } : run.Plan };
        var blocked=new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.NotEqual(CoachActionKind.Craft,blocked.Kind);
        Assert.Equal(1,frame.Inventory[common]);
    }

    [Theory]
    [InlineData(null,"540h",false)]
    [InlineData("MC0h","MC0h",false)]
    [InlineData("540h","540h",false)] // Auxiliary Kid slow does not veto the ready boss craft.
    [InlineData("540h","540h",true)]
    public void CharacterizationG18BossRecipeProgressAndTie(string? prepared,string expected,bool backup)
    {
        var inventory=Counts("180h","U30h");
        if(prepared is not null)
            foreach(var p in catalog.Unit("rawcode:"+prepared).Recipe) inventory[p.Key]=p.Value;
        if(backup) inventory["rawcode:D20h"]=2;
        var result=Run(inventory);
        Assert.Equal("rawcode:"+expected,result.Plan.TargetUnitId);
        Assert.Contains("보스/광폭화",result.Decision.OperationGuide);
        Assert.DoesNotContain("공격 중",result.Decision.Controls);
    }

    [Theory]
    [MemberData(nameof(SpecialRows))]
    public void CharacterizationNamedSpecialSpareAndRecipeReservation(string retained,string target,string group,int armor,int slow)
    {
        Assert.NotEmpty(group); Assert.True(armor>=0 && slow>=0);
        var inventory=catalog.Unit("rawcode:"+target).Recipe.ToDictionary(p=>p.Key,p=>p.Value*2);
        inventory["rawcode:180h"]=1;
        var available=Run(inventory,40,target:"rawcode:"+target);
        Assert.Equal(CoachActionKind.Craft,available.Decision.Kind);
        inventory["rawcode:"+retained]=1;
        var reserved=Run(inventory,40,target:"rawcode:"+target,roots:["rawcode:"+retained]);
        Assert.NotEqual(CoachActionKind.Craft,reserved.Decision.Kind);
    }

    [Theory]
    [InlineData("C20h","F30h",1,false)] [InlineData("C20h","F30h",2,true)]
    [InlineData("V10h","I30h",1,false)] [InlineData("V10h","I30h",2,true)]
    [InlineData("E20h","Q30h",1,false)] [InlineData("E20h","Q30h",2,true)]
    [InlineData("K20h","S30h",1,true)] [InlineData("K20h","S30h",2,true)]
    public void CharacterizationG32RareLastRoleAndSpare(string rare,string target,int count,bool craft)
    {
        var inventory=catalog.Unit("rawcode:"+target).Recipe.ToDictionary(p=>p.Key,p=>p.Value*2);
        inventory["rawcode:180h"]=1; inventory["rawcode:"+rare]=count;
        var result=Run(inventory,40,target:"rawcode:"+target);
        Assert.True(craft==(result.Decision.Kind==CoachActionKind.Craft),$"{rare}: {result.Decision.Kind} {result.Decision.Controls}");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ProtectedBodyCannotBeReassignedToSelectedActivePromotion(int count, bool craft)
    {
        var inventory=catalog.Unit("rawcode:630h").Recipe.ToDictionary(p=>p.Key,p=>p.Value*2);
        inventory["rawcode:180h"]=1; inventory["rawcode:B20h"]=count;
        var run=Run(inventory,40,target:"rawcode:630h",roots:["rawcode:B20h"]);
        // Preserve the guarded pipeline result, then exercise final revalidation of a
        // real previously planned action (before the new protection was attached).
        Assert.Equal(craft, run.Decision.Kind==CoachActionKind.Craft);
        var proposed=Run(inventory,40,target:"rawcode:630h");
        Assert.Equal(CoachActionKind.Craft,proposed.Decision.Kind);
        var result=new BeginnerCoachPlanner(catalog).Decide(run.Frame with {
            SelectedGoalIds=["rawcode:630h"], CraftSteps=proposed.Frame.CraftSteps });
        Assert.True(craft==(result.Kind==CoachActionKind.Craft),$"B20h={count}: {result.Kind} {result.Controls}");
    }

    [Theory]
    [InlineData(1, false, false)] [InlineData(1, false, true)]
    [InlineData(1, true, false)] [InlineData(1, true, true)]
    [InlineData(2, false, false)] [InlineData(2, false, true)]
    [InlineData(2, true, false)] [InlineData(2, true, true)]
    public void ProtectedBodyOwnershipIsIndependentOfActiveDuplicationAndOrder(int count, bool selected, bool reverse)
    {
        var inventory=catalog.Unit("rawcode:630h").Recipe.ToDictionary(p=>p.Key,p=>p.Value*2);
        inventory["rawcode:180h"]=1; inventory["rawcode:B20h"]=count;
        var proposed=Run(inventory,40,target:"rawcode:630h");
        Assert.Equal(CoachActionKind.Craft,proposed.Decision.Kind);
        var roots=new[]{"rawcode:B20h","rawcode:180h"};
        if(reverse) roots=roots.Reverse().ToArray();
        var frame=proposed.Frame with {
            GuidePlan=proposed.Plan with { ProtectedUnitIds=roots },
            SelectedGoalIds=selected ? (reverse ? ["rawcode:B20h","rawcode:630h","rawcode:630h"] : ["rawcode:630h","rawcode:630h","rawcode:B20h"]) : [] };
        var available=BulletGuideReservations.Available(catalog,inventory,
            frame.GuidePlan.ProtectedUnitIds.Concat(frame.SelectedGoalIds),"rawcode:630h");
        Assert.Equal(count-1,available.GetValueOrDefault("rawcode:B20h"));
        Assert.Equal(0,available.GetValueOrDefault("rawcode:180h"));
        var decision=new BeginnerCoachPlanner(catalog).Decide(frame);
        Assert.Equal(count==2,decision.Kind==CoachActionKind.Craft);
        if(count==2) Assert.Equal("rawcode:630h",decision.TargetUnitId);
    }

    [Theory]
    [InlineData("D10h",true)] [InlineData("F10h",false)]
    public void ProtectedBodyFixPreservesOwnIntermediateAndCompetingCommit(string committed,bool craft)
    {
        var inventory=Ready(); inventory["luffy_common"]=30;
        foreach(var p in catalog.Unit("rawcode:K20h").Recipe) inventory[p.Key]=p.Value;
        inventory.Remove("rawcode:D10h");
        foreach(var p in catalog.Unit("rawcode:D10h").Recipe) inventory[p.Key]=p.Value;
        var run=Run(inventory);
        Assert.Equal(CoachActionKind.Craft,run.Decision.Kind);
        Assert.Equal("rawcode:D10h",run.Decision.TargetUnitId);
        var decision=new BeginnerCoachPlanner(catalog).Decide(run.Frame with {
            SelectedGoalIds=["rawcode:K20h"],CommittedCraftUnitId="rawcode:"+committed });
        Assert.Equal(craft,decision.Kind==CoachActionKind.Craft);
        if(craft) Assert.Equal("rawcode:D10h",decision.TargetUnitId);
    }

    [Theory]
    [InlineData("B20h","630h")] [InlineData("X90h","HA0h")]
    public void SourceAllowedPromotionStillRespectsJointReservation(string rare,string target)
    {
        var inventory=catalog.Unit("rawcode:"+target).Recipe.ToDictionary(p=>p.Key,p=>p.Value*2);
        inventory["rawcode:180h"]=1; inventory["rawcode:"+rare]=1;
        var result=Run(inventory,40,target:"rawcode:"+target,roots:["rawcode:"+rare]);
        Assert.NotEqual(CoachActionKind.Craft,result.Decision.Kind);
    }

    [Theory]
    [InlineData("O30h","IC0h")] [InlineData("O30h","W20h")]
    [InlineData("Y30h","O30h")] [InlineData("Z20h",null)]
    public void CharacterizationPreparedSourceStunFamiliesReachActualCraft(string target,string? partner)
    {
        var inventory=Counts("U20h","V20h","930h","U30h","HA0h","MC0h");
        if(partner is not null) inventory["rawcode:"+partner]=1;
        foreach(var p in catalog.Unit("rawcode:"+target).Recipe) inventory[p.Key]=p.Value*2;
        var result=Run(inventory,40);
        Assert.Equal("rawcode:"+target,result.Plan.TargetUnitId);
        Assert.Equal(CoachActionKind.Craft,result.Decision.Kind);
        Assert.Equal("rawcode:"+target,result.Decision.TargetUnitId);
        var allocation=new RecipeCompletionCalculator(catalog.Unit).CalculateAllocation(["rawcode:"+target],inventory);
        var after=allocation.RemainingInventory.ToDictionary(p=>p.Key,p=>checked((int)p.Value));
        after["rawcode:"+target]=1;
        Assert.True(new BulletGuideSupportPolicy(catalog).Evaluate(after,BulletGuidePolicy.NavigationId,GoroseiMode.None).StunPairReady);
        if(partner is not null) Assert.True(after.GetValueOrDefault("rawcode:"+partner)>0);
    }

    [Theory]
    [InlineData(11,0)] [InlineData(11,1)] [InlineData(11,2)] [InlineData(12,2)]
    public void CharacterizationG15MaterialsBeforeStoryTwelveAreNotPostCraftSupport(int story,int components)
    {
        var inventory=Ready(); inventory.Remove("rawcode:180h"); inventory["rawcode:HA0h"]=1;
        foreach(var code in new[]{"U20h","V20h"}.Take(components)) inventory["rawcode:"+code]=1;
        var result=Run(inventory,40,story);
        Assert.Equal(components,result.Plan.ComponentCount);
        Assert.False(result.Plan.OwnedBullet);
        Assert.NotEqual(BulletGuidePolicy.GoalId,result.Decision.TargetUnitId);
        Assert.Contains("50라",result.Decision.OperationGuide);
        // Planned/consumed Smoker must never add its 50 slow to post-craft capacity.
        var withoutComponents=inventory.Where(p=>p.Key is not ("rawcode:U20h" or "rawcode:V20h")).ToDictionary(p=>p.Key,p=>p.Value);
        Assert.Equal(new BulletGuideSupportPolicy(catalog).Evaluate(withoutComponents,BulletGuidePolicy.NavigationId,GoroseiMode.None).SlowPotential,result.Plan.Support!.SlowPotential);
    }

    [Theory]
    [InlineData("BestHelp.MaximumOutput")] [InlineData("AlliedForces.DoubleBenefit")]
    public void CharacterizationG54AuthorPreferenceDoesNotInvalidateConfirmedNavigation(string navigation)
    {
        var run=Run(Ready());
        var plan=new BulletGuidePolicy(catalog).Plan(50,13,run.Frame.Inventory,"악몽",navigation);
        var decision=new BeginnerCoachPlanner(catalog).Decide(run.Frame with { ConfirmedNavigation=navigation,GuidePlan=plan });
        Assert.NotEqual(CoachActionKind.Navigation,decision.Kind);
        Assert.Contains("선택 불가 판정이 아닙니다",decision.OperationGuide);
        Assert.DoesNotContain("선택 불가",decision.Controls);
    }

    [Theory]
    [InlineData(null)] [InlineData(0)]
    [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)]
    public void CharacterizationG55FailuresAndStaticCostDoNotBecomeSpendInstruction(int? failures)
    {
        var run=Run(Ready());
        var plan=run.Plan with { ActiveHighGambleQuest=true,HighGamble=new(failures is not null,true,failures,"fixture") };
        var decision=new BeginnerCoachPlanner(catalog).Decide(run.Frame with { GuidePlan=plan });
        Assert.Equal(failures, plan.HighGamble.Failures);
        Assert.Equal(failures is not null, plan.HighGamble.IsVerified);
        Assert.Contains(HighGamblePresentation.Describe(plan.HighGamble), decision.OperationGuide);
        Assert.Equal(2000, HighGamblePresentation.GoldCost);
        Assert.Equal(4, HighGamblePresentation.LumberCost);
        Assert.Equal("R00G", HighGamblePresentation.RequiredTech);
        Assert.Equal(run.Decision.Id, decision.Id);
        Assert.Equal(run.Decision.Kind,decision.Kind);
        Assert.Equal(run.Decision.Controls,decision.Controls);
    }

    [Fact]
    public void ActiveChopperRecipeCanUseItsOwnCommittedIntermediate()
    {
        var inventory=Ready(); inventory["luffy_common"]=30;
        foreach(var p in catalog.Unit("rawcode:K20h").Recipe) inventory[p.Key]=p.Value;
        inventory.Remove("rawcode:D10h");
        foreach(var p in catalog.Unit("rawcode:D10h").Recipe) inventory[p.Key]=p.Value;
        var run=Run(inventory);
        Assert.Equal("rawcode:D10h",run.Decision.TargetUnitId);
        Assert.Equal(CoachActionKind.Craft,run.Decision.Kind);
        var result=new BeginnerCoachPlanner(catalog).Decide(run.Frame with { CommittedCraftUnitId="rawcode:D10h" });
        Assert.Equal(CoachActionKind.Craft,result.Kind);
        Assert.Equal("rawcode:D10h",result.TargetUnitId);
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    public void CharacterizationFinalBulletRoundBoundary(int round, bool craft)
    {
        var inventory = Ready(); inventory.Remove("rawcode:180h");
        foreach (var code in new[]{"930h","V20h","U20h"}) inventory["rawcode:"+code]=1;
        var result = Run(inventory, round);
        Assert.Equal(craft, result.Decision.Kind == CoachActionKind.Craft);
        if(craft) Assert.Equal(BulletGuidePolicy.GoalId, result.Decision.TargetUnitId);
    }
}
