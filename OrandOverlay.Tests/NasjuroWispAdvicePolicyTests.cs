using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;

public sealed class NasjuroWispAdvicePolicyTests
{
    internal static DataCatalog Catalog() { var c = new DataCatalog(); c.Load(loadCarryPolicy: false); return c; }
    internal static CoachFrame Frame(GoroseiMode mode = GoroseiMode.Nasjuro)
    {
        var inventory = new[] { "U20h", "V20h", "3A0h" }.ToImmutableDictionary(c => "rawcode:" + c, _ => 1);
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.Accept(1, 1, mode, true);
        return new CoachFrame {
            Mode = PlayMode.Guide, GuideNumber = 1, MatchGeneration = 1, Revision = 1,
            RecognitionRevision = 1, Gorosei = session.Current,
            Round = 25, CompletedStoryStage = 10, Difficulty = "악몽", IsCurrent = true,
            ConfirmedNavigation = BulletGuidePolicy.NavigationId, Inventory = inventory,
            GuidePlan = new BulletGuidePolicy(Catalog()).Plan(25, 10, inventory, "악몽", BulletGuidePolicy.NavigationId, mode),
            CombatObservations = [new(1, "h0A3", 0, null, CombatUnitKind.LocalUnit, null, 100, 100, null, false, false)],
            RewardWisps = ImmutableDictionary<string, int>.Empty.Add("e01A", 1)
        };
    }
    [Fact]
    public void SharedSnapshotFeedsAllConsumersAndStaleInputsAreRejected()
    {
        var f = Frame() with { GoalId = BulletGuidePolicy.GoalId };
        f = f with { ShipReservations = ShipReservationPolicy.Evaluate(f,Catalog()) };
        Assert.Equal(NasjuroApplicability.Applicable,NasjuroWispAdvicePolicy.Evaluate(f).Applicability);
        Assert.Contains("8:2",BulletGuideAdvice.Operation(f));
        Assert.Null(BulletGuideRayleighPolicy.Decide(f));
        Assert.Contains("8:2",NasjuroWispAdvicePolicy.Decide(f,Catalog())!.OperationGuide);
        var changed = f with { SelectedGoalIds = ["rawcode:850h"] };
        Assert.False(ShipReservationPolicy.Matches(f.ShipReservations!,changed));
        Assert.Equal(NasjuroApplicability.Unknown,NasjuroWispAdvicePolicy.Evaluate(changed).Applicability);
        var recomputed = new BeginnerCoachPlanner(Catalog()).Decide(changed);
        Assert.Contains("추가 배 부족",recomputed.OperationGuide);
        Assert.DoesNotContain("8:2",recomputed.OperationGuide);
        Assert.False(ShipReservationPolicy.Matches(f.ShipReservations!,f with { Inventory = f.Inventory.Add("rawcode:060h",1) }));
        Assert.False(ShipReservationPolicy.Matches(f.ShipReservations!,f with { RecognitionRevision = 2 }));
    }
    [Fact]
    public void ProtectionRoleChangeInvalidatesSnapshotEvenWhenRootUnionIsSame()
    {
        var f = Frame() with { SelectedGoalIds = ["rawcode:U30h","rawcode:2B0H"] };
        var snapshot = ShipReservationPolicy.Evaluate(f,Catalog());
        var changed = f with { GuidePlan = f.GuidePlan! with { ProtectedUnitIds = f.GuidePlan.ProtectedUnitIds.Concat(new[] { "rawcode:U30h" }).ToArray() } };
        Assert.False(ShipReservationPolicy.Matches(snapshot,changed));
    }
    [Fact]
    public void SpentWispYieldsToActualPlannedUpgradeWhileKeepingDirection()
    {
        var f = BulletGuideUncommonSaleTests.PlannedFrame(BulletGuideUncommonSaleTests.OperatingSupportCodes().Concat(new[] { "3A0h" }).ToArray(), Frame().CombatObservations[0], GoroseiMode.Nasjuro);
        f = f with { Gorosei = Frame().Gorosei, RecognitionRevision = 1,
            GuideRuntime = new(true,2,1,1,"fixture current tiers"), RewardWisps = ImmutableDictionary<string,int>.Empty.Add("e01A",1) };
        var planner = new BeginnerCoachPlanner(Catalog());
        Assert.Contains("nasjuro-wisp",planner.Decide(f).Id);
        var spent = planner.Decide(f with { RewardWisps = ImmutableDictionary<string,int>.Empty });
        Assert.Equal("guide1:upgrade:armor",spent.Id);
        Assert.Contains("8:2",spent.OperationGuide);
    }
    [Theory]
    [InlineData("unknown")][InlineData("missing")]
    public void DemandChangesActualDirectionRatherThanDecoratingWoodPreference(string demand)
    {
        var f = Frame() with { SelectedGoalIds = [demand == "unknown" ? "rawcode:????" : "rawcode:850h"] };
        var advice = NasjuroWispAdvicePolicy.Evaluate(f,ShipReservationPolicy.Evaluate(f,Catalog()));
        Assert.DoesNotContain("8:2",advice.Guidance);
        Assert.DoesNotContain("더 자주 고려",advice.Guidance);
        Assert.Contains(demand == "unknown" ? "수요 미확인" : "추가 배 부족",advice.Guidance);
        Assert.True(advice.SuppressGenericRayleigh);
        Assert.Null(BulletGuideRayleighPolicy.Decide(f));
    }
    [Theory]
    [InlineData("infinity")][InlineData("nan")][InlineData("zeroGeneration")]
    public void NonFiniteLifeAndInvalidGenerationNeverProveActual(string invalid)
    {
        var f = Frame();
        f = invalid == "zeroGeneration" ? f with { MatchGeneration = 0, Gorosei = f.Gorosei with { MatchGeneration = 0 } } :
            f with { CombatObservations = [f.CombatObservations[0] with { Life = invalid == "infinity" ? float.PositiveInfinity : float.NaN }] };
        Assert.Equal(NasjuroApplicability.Unknown,NasjuroWispAdvicePolicy.Evaluate(f,ShipReservationPolicy.Evaluate(f,Catalog())).Applicability);
    }
    [Theory]
    [InlineData("dead")][InlineData("lifeUnknown")][InlineData("foreign")][InlineData("mirror")]
    [InlineData("missing")][InlineData("wrongNative")][InlineData("generation")][InlineData("revision")][InlineData("none")]
    public void UncertainActualBodyProtectsAndNeverUsesGenericFallback(string reason)
    {
        var f = Frame(); var u = f.CombatObservations[0];
        f = reason switch {
            "dead" => f with { CombatObservations = [u with { Life = 0 }] },
            "lifeUnknown" => f with { CombatObservations = [u with { Life = null }] },
            "foreign" => f with { CombatObservations = [u with { Owner = 7, Kind = CombatUnitKind.RecipeExemplar }] },
            "mirror" => f with { CombatObservations = [u with { MirrorCopy = true }] },
            "missing" => f with { CombatObservations = [] },
            "wrongNative" => f with { CombatObservations = [u with { Rawcode = "h043" }] },
            "generation" => f with { Gorosei = f.Gorosei with { MatchGeneration = 2 } },
            "revision" => f with { RecognitionRevision = 2 },
            _ => f with { Gorosei = GoroseiObservation.Unknown } };
        var allocation = ShipReservationPolicy.Evaluate(f,Catalog());
        var advice = NasjuroWispAdvicePolicy.Evaluate(f,allocation);
        Assert.Equal(NasjuroApplicability.Unknown,advice.Applicability);
        Assert.DoesNotContain("8:2",advice.Guidance);
        Assert.Null(BulletGuideRayleighPolicy.Decide(f));
        Assert.NotEqual("guide1:select-rayleigh-ship",new BeginnerCoachPlanner(Catalog()).Decide(f).Id);
    }
    [Theory]
    [InlineData("hidden")][InlineData("eternal")][InlineData("planned")][InlineData("greenblood")][InlineData("warcury")]
    public void ConfirmedNonApplicableDeckKeepsGenericRayleighPositive(string reason)
    {
        var f = Frame(reason == "warcury" ? GoroseiMode.Warcury : GoroseiMode.Nasjuro);
        if (reason != "warcury") f = f with { Inventory = f.Inventory.Remove("rawcode:3A0h"), CombatObservations = [] };
        f = reason switch {
            "hidden" => f with { Inventory = f.Inventory.Add("rawcode:340h",1) },
            "eternal" => f with { Inventory = f.Inventory.Add("rawcode:850h",1) },
            "planned" => f with { SelectedGoalIds = ["rawcode:3A0h"] },
            "greenblood" => f with { GreenBloodAvailable = true }, _ => f };
        f = f with { GuidePlan = new BulletGuidePolicy(Catalog()).Plan(25,10,f.Inventory,"악몽",BulletGuidePolicy.NavigationId,f.Gorosei.Mode) };
        Assert.False(NasjuroWispAdvicePolicy.Evaluate(f).SuppressGenericRayleigh);
        Assert.NotNull(BulletGuideRayleighPolicy.Decide(f));
    }
    [Fact]
    public void DirectionDoesNotRequireWispOrLedgerAndNoneRemovesActualClaim()
    {
        var f = Frame() with { RewardWisps = ImmutableDictionary<string,int>.Empty };
        var planner = new BeginnerCoachPlanner(Catalog());
        Assert.Contains("8:2",planner.Decide(f).OperationGuide);
        var session = new GoroseiObservationSession(); session.Reset(1);
        session.Accept(1,1,GoroseiMode.Nasjuro,true); session.Accept(1,2,GoroseiMode.None,true);
        f = f with { RecognitionRevision = 2, Gorosei = session.Current };
        Assert.DoesNotContain("8:2",planner.Decide(f).OperationGuide);
        Assert.Contains("미확인",planner.Decide(f).OperationGuide);
    }
    [Theory]
    [InlineData("pause")][InlineData("clear")][InlineData("fail")][InlineData("difficulty")][InlineData("stale")]
    public void ExistingPriorityGatesDisableOperatingAdvice(string gate)
    {
        var f = Frame(); f = gate switch { "pause" => f with { Paused = true }, "clear" => f with { Outcome = "clear" },
            "fail" => f with { Outcome = "fail" }, "difficulty" => f with { Difficulty = "unknown" }, _ => f with { IsCurrent = false } };
        Assert.False(NasjuroWispAdvicePolicy.Evaluate(f).SuppressGenericRayleigh);
        Assert.DoesNotContain("nasjuro-wisp",new BeginnerCoachPlanner(Catalog()).Decide(f).Id);
    }
    [Fact]
    public void RewardAndProtectedAncientHoldCannotFallThroughToRayleigh()
    {
        var f = Frame();
        Assert.Equal("e019",new BeginnerCoachPlanner(Catalog()).Decide(f with { RewardWisps = f.RewardWisps.Add("e019",1) }).RewardWispId);
        f = f with { SelectedGoalIds = ["rawcode:780h"], Inventory = f.Inventory.Add(ShipReservationPolicy.Ancient,1),
            Signals = f.Signals.Add("lumber",100), CombatObservations = f.CombatObservations.Add(
                new(2,"h05Y",0,null,CombatUnitKind.LocalUnit,null,100,100,null,false,false) { AncientShipAbility = new("A0KB",1,0) }) };
        Assert.Null(BulletGuideAncientShipPolicy.Decide(f,Catalog()));
        Assert.Null(BulletGuideRayleighPolicy.Decide(f));
        Assert.Contains("nasjuro-wisp",new BeginnerCoachPlanner(Catalog()).Decide(f).Id);
    }
    [Fact]
    public void OtherGoalsKeepBothShipReservationsInActualPlanner()
    {
        var f = Frame();
        f = f with { SelectedGoalIds = ["rawcode:780h", "rawcode:850h"],
            Inventory = f.Inventory.Add("rawcode:Y50h", 1).Add("rawcode:060h", 2) };
        var d = new BeginnerCoachPlanner(Catalog()).Decide(f);
        Assert.Equal(1, d.PreservedMaterialCounts.GetValueOrDefault("rawcode:Y50h"));
        Assert.Equal(2, d.PreservedMaterialCounts.GetValueOrDefault("rawcode:060h"));
        Assert.Contains("토키", d.PreservedMaterials);
        Assert.Contains("미호크", d.PreservedMaterials);
    }
    [Fact]
    public void ActualPlanNasjuroAndLiveSeraphimSuppressForcedRayleighAndShowConditionalDirection()
    {
        var frame = Frame();
        Assert.Equal(BulletGuideStage.AirFoundation, frame.GuidePlan!.Stage);
        var decision = new BeginnerCoachPlanner(Catalog()).Decide(frame);
        Assert.NotEqual("guide1:select-rayleigh-ship", decision.Id);
        Assert.Contains("8:2", decision.OperationGuide);
        Assert.Contains("대략적", decision.OperationGuide);
        Assert.Contains("메뉴 미확인", decision.UnknownSignals);
        Assert.Null(decision.TargetUnitId);
        Assert.Null(decision.RewardWispId);
        Assert.Null(BulletGuideRayleighPolicy.Decide(frame));
    }
}
