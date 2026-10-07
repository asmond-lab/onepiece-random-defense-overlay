using System.Collections.Immutable;
using Xunit;
namespace OrandOverlay.Tests;
public sealed class BulletGuideAncientShipTests
{
    internal static CoachFrame Frame()
    {
        var f = BulletGuideUncommonSaleTests.Frame("h05Y", "rawcode:Y50h");
        var ship = f.CombatObservations[0] with { AncientShipAbility = new("A0KB", 1, 0), UncommonSaleAbility = null };
        return f with { GuidePlan = new(BulletGuideStage.ControlSupport,"rawcode:Q30h",true),
            Signals = ImmutableDictionary<string,long?>.Empty.Add("lumber",8),
            CombatObservations = [ship] };
    }
    [Theory]
    [InlineData("wood7")][InlineData("woodUnknown")][InlineData("shipGone")][InlineData("targetMet")]
    [InlineData("noTarget")][InlineData("stale")][InlineData("pause")][InlineData("fail")]
    [InlineData("difficulty")][InlineData("reward")][InlineData("abilityUnknown")][InlineData("cooldown")]
    [InlineData("gamble")][InlineData("wispAlternative")][InlineData("toki")][InlineData("committedToki")]
    [InlineData("protected")][InlineData("woodReserved")][InlineData("foreign")][InlineData("dead")]
    public void UnsafeOrUnneededConversionDoesNotConsumeShip(string blocked)
    {
        var f = Frame(); var u=f.CombatObservations[0];
        f = blocked switch {
            "wood7" => f with { Signals=f.Signals.SetItem("lumber",7) },
            "woodUnknown" => f with { Signals=f.Signals.SetItem("lumber",null) },
            "shipGone" => f with { Inventory=f.Inventory.Remove("rawcode:Y50h") },
            "targetMet" => f with { Inventory=f.Inventory.Add("rawcode:060h",1) },
            "noTarget" => f with { GuidePlan=f.GuidePlan! with { TargetUnitId=null } },
            "stale" => f with { IsCurrent=false }, "pause" => f with { Paused=true }, "fail" => f with { Outcome="fail" },
            "difficulty" => f with { Difficulty="unknown" }, "reward" => f with { RewardWisps=f.RewardWisps.Add("e019",1) },
            "abilityUnknown" => f with { CombatObservations=[u with { AncientShipAbility=null }] },
            "cooldown" => f with { CombatObservations=[u with { AncientShipAbility=new("A0KB",1,null) }] },
            "gamble" => f with { CombatObservations=[u with { AncientShipAbility=new("A0KC",1,0) }] },
            "wispAlternative" => f with { CombatObservations=[u with { AncientShipAbility=new("A0OE",1,0) }] },
            "toki" => f with { SelectedGoalIds=["rawcode:780h"] },
            "committedToki" => f with { CommittedCraftUnitId="rawcode:780h" },
            "protected" => f with { GuidePlan=f.GuidePlan! with { ProtectedUnitIds=["rawcode:Y50h"] } },
            "woodReserved" => f with { SelectedGoalIds=[BulletGuidePolicy.GoalId], Inventory=f.Inventory.Remove(BulletGuidePolicy.GoalId) },
            "foreign" => f with { CombatObservations=[u with { Kind=CombatUnitKind.RecipeExemplar,Owner=7 }] },
            "dead" => f with { CombatObservations=[u with { Life=0 }] }, _ => f };
        Assert.NotEqual("guide1:ancient-to-pirate",new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(f).Id);
        Assert.False(f.Inventory.ContainsKey("rawcode:X50h"));
    }
    [Theory]
    [InlineData(-1,false)][InlineData(0,true)][InlineData(1,true)]
    public void ConversionCostIsChargedOnceOutsideUnfinishedRecipeLumberReservation(int delta,bool expected)
    {
        var c=BulletGuideUncommonSaleTests.Catalog();
        var f=Frame();
        f=f with { Inventory=f.Inventory.Remove(BulletGuidePolicy.GoalId), SelectedGoalIds=[BulletGuidePolicy.GoalId],
            GuidePlan=f.GuidePlan! with { OwnedBullet=false } };
        var calculator=new RecipeCompletionCalculator(c.Unit);
        var reserved=calculator.CalculateAllocation([BulletGuidePolicy.GoalId,"rawcode:Q30h"],f.Inventory).ResourceRequirements.Lumber;
        Assert.Equal(19,reserved);
        Assert.Equal(0,calculator.CalculateAllocation(["rawcode:060h"],f.Inventory).ResourceRequirements.Lumber);
        f=f with { Signals=f.Signals.SetItem("lumber",8+reserved+delta) };
        Assert.Equal(expected,BulletGuideAncientShipPolicy.Decide(f,c) is not null);
    }
    [Fact]
    public void OwnedIntermediateMaterialsDoNotReserveTheirAlreadyPaidLumberAgain()
    {
        var f=Frame();
        f=f with { Inventory=f.Inventory.Remove(BulletGuidePolicy.GoalId).Add("rawcode:U20h",1).Add("rawcode:V20h",1).Add("rawcode:930h",1),
            SelectedGoalIds=[BulletGuidePolicy.GoalId], GuidePlan=f.GuidePlan! with { OwnedBullet=false },
            Signals=f.Signals.SetItem("lumber",8+10) };
        Assert.NotNull(BulletGuideAncientShipPolicy.Decide(f,BulletGuideUncommonSaleTests.Catalog()));
        Assert.Null(BulletGuideAncientShipPolicy.Decide(f with { Signals=f.Signals.SetItem("lumber",8+10-1) },BulletGuideUncommonSaleTests.Catalog()));
    }
    [Fact]
    public void AncientToPirateShipRequiresOwnedShipAndWood8()
    {
        var frame = Frame();
        var result = new BeginnerCoachPlanner(BulletGuideUncommonSaleTests.Catalog()).Decide(frame);
        Assert.Equal("guide1:ancient-to-pirate",result.Id);
        Assert.Equal("rawcode:Y50h",result.ConsumedUnitId);
        Assert.Equal("rawcode:060h",result.TargetUnitId);
        Assert.Equal(8,frame.Signals["lumber"]);
        Assert.False(frame.Inventory.ContainsKey("rawcode:060h"));
    }
}
