using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Map2320NavigationTests
{
    private static string DataFile => Path.Combine(AppContext.BaseDirectory,"Data","navigation-mechanics-2320.json");
    [Fact] public void CatalogIsPinnedAndHasFifteenNewSourceOptions()
    {
        var d=Map2320NavigationMechanics.Load(DataFile);
        Assert.Equal(15,d.Options.Length); Assert.Equal(7,d.Exchanges.Length);
        Assert.Contains(d.Options,x=>x.Id=="Gambler.Exchange2320" && x.Name=="환전소");
        Assert.DoesNotContain(d.Options,x=>x.Id.Contains("ContinuousBetting"));
        Assert.All(d.Options,o=>{Assert.Equal("Unknown",o.ScoringStatus); Assert.All(o.SourceFunctions,f=>Assert.Contains(d.Evidence,e=>e.Function==f && e.Text.StartsWith("function "+f+" ")));});
        Assert.All(d.Exchanges,x=>{Assert.Null(x.ManaCost); Assert.Equal("UnknownInheritedANcl",x.ManaStatus); Assert.InRange(x.CooldownSeconds,.199,.201);Assert.Equal("Unknown",x.ProbabilityStatus);});
    }
    [Fact] public void TamperingOrUnknownFieldsRejected()
    {
        var p=Path.GetTempFileName(); try { File.WriteAllText(p," " + File.ReadAllText(DataFile)); Assert.Throws<InvalidDataException>(()=>Map2320NavigationMechanics.Load(p)); } finally { File.Delete(p); }
    }
    [Theory] [InlineData("A0CI",1)] [InlineData("A0EZ",3)] [InlineData("A0DB",3)] [InlineData("A0IY",5)] [InlineData("A0JR",3)] [InlineData("A0BV",10)] [InlineData("A0MA",20)]
    public void ExactDebits(string ability,int cost)
    {
        var state=new Map2320ExchangeState(cost,0,0,true,Map2320Mode.Otherworld);
        Assert.Equal(0,Map2320NavigationMechanics.Exchange(ability,state).PointsAfter);
        Assert.Equal(Map2320Status.InsufficientPoints,Map2320NavigationMechanics.Exchange(ability,state with {Points=cost-1}).Status);
    }
    [Theory] [InlineData("A0JR",3)] [InlineData("A0BV",10)]
    public void FullCapStillDebitsAndDisables(string ability,int cost)
    {
        var r=Map2320NavigationMechanics.Exchange(ability,new(20,10,10,true,Map2320Mode.Normal));
        Assert.Equal(20-cost,r.PointsAfter); Assert.Equal(10,r.StacksAfter); Assert.True(r.DisableAfterUse); Assert.Equal(0,Assert.Single(r.Alternatives).Maximum); Assert.False(r.IsSafeRecommendation);
    }
    [Fact] public void StackBelowCapAndDisabledAction()
    {
        Assert.Equal(10,Map2320NavigationMechanics.Exchange("A0BV",new(10,0,9,true,Map2320Mode.Normal)).StacksAfter);
        Assert.Equal(Map2320Status.Disabled,Map2320NavigationMechanics.Exchange("A0BV",new(10,0,9,false,Map2320Mode.Normal)).Status);
        Assert.Equal(Map2320Status.Unknown,Map2320NavigationMechanics.Exchange("A0CI",new(10,0,0,true,Map2320Mode.Unknown)).Status);
        Assert.Equal(Map2320Status.Ineligible,Map2320NavigationMechanics.Exchange("A0MA",new(20,0,0,true,Map2320Mode.Normal)).Status);
    }
    [Theory] [InlineData(true)] [InlineData(false)] public void AttemptPointPrecedesOutcome(bool success) => Assert.Equal(8,Map2320NavigationMechanics.IntermediateAttempt(7,true,success).Value);
    [Fact] public void UnknownOutcomeStillCreditsKnownAttemptAndOverflowFailsClosed()
    {
        Assert.Equal(1,Map2320NavigationMechanics.IntermediateAttempt(0,true,null).Value);
        Assert.Equal(Map2320Status.Unknown,Map2320NavigationMechanics.IntermediateAttempt(0,null,null).Status);
        Assert.Equal(Map2320Status.Overflow,Map2320NavigationMechanics.IntermediateAttempt(int.MaxValue,true,false).Status);
        Assert.Equal(Map2320Status.Overflow,Map2320NavigationMechanics.CasinoSelection(int.MaxValue,1,Map2320Mode.Otherworld).Status);
    }
    [Fact] public void CasinoCompensatesConsumedAndFutureGrantDoublesBaseOne()
    {
        Assert.Equal(11,Map2320NavigationMechanics.CasinoSelection(4,3,Map2320Mode.Otherworld).Value);
        Assert.Equal(2,Map2320NavigationMechanics.FutureWorldGrant(0,true).Value);
        Assert.Equal(1,Map2320NavigationMechanics.FutureWorldGrant(0,false).Value);
        Assert.Equal(Map2320Status.Unknown,Map2320NavigationMechanics.CasinoSelection(4,3,Map2320Mode.Unknown).Status);
    }
    [Fact] public void RiskThresholdAndReset()
    {
        var fail=Map2320NavigationMechanics.WorldAttempt(3,2,0,true,false,Map2320Mode.Otherworld,16);
        Assert.Equal(15L,fail.SuccessThreshold); Assert.Equal(15,fail.Risk); Assert.Equal(2,fail.Remaining); Assert.Equal(3,fail.Consumed);
        var success=Map2320NavigationMechanics.WorldAttempt(2,3,15,true,false,Map2320Mode.Otherworld,30);
        Assert.Equal(0,success.Risk);
        Assert.Equal(0,Map2320NavigationMechanics.WorldAttempt(1,0,90,true,false,Map2320Mode.Otherworld,100).Risk);
        Assert.Equal(Map2320Status.Unknown,Map2320NavigationMechanics.WorldAttempt(1,0,90,true,false,Map2320Mode.Otherworld,null).Status);
    }
    [Fact] public void LeoIsLevelDependentNotLegacyReward()
    {
        var normal=Map2320NavigationMechanics.Leo(false); var martial=Map2320NavigationMechanics.Leo(true,3);
        Assert.Equal(10,normal.ConfiguredSlowPercent); Assert.Equal(20,martial.ConfiguredSlowPercent); Assert.Equal(850,martial.AuraRadius); Assert.Equal(1200000,martial.AttackDamage); Assert.True(martial.StunStage); Assert.Equal(0,martial.NextCycle);
        Assert.Equal(Map2320Status.Ineligible,Map2320NavigationMechanics.PathSelection(true,1)); Assert.Equal(Map2320Status.Bounded,Map2320NavigationMechanics.PathSelection(false,1));
        var effects=string.Join(" ",Map2320NavigationMechanics.Load(DataFile).Options.SelectMany(x=>x.Effects));
        Assert.DoesNotContain("clear_berry_multiplier:2",effects); Assert.DoesNotContain("boss_lumber:2",effects); Assert.DoesNotContain("line_movement_reduction:7",effects);
    }
    [Fact] public void RetroactiveCounterSumSeparateFromFuturePredicate()
    {
        Assert.Equal(9,Map2320NavigationMechanics.DoubleBenefitRetroactive(new(1,1,1,1,1,1,1,1,1)).Value);
        Assert.Equal(Map2320Status.Overflow,Map2320NavigationMechanics.DoubleBenefitRetroactive(new(int.MaxValue,1,0,0,0,0,0,0,0)).Status);
        Assert.Equal(0,Map2320NavigationMechanics.FutureCraftWisps(100,1).Value); Assert.Equal(2,Map2320NavigationMechanics.FutureCraftWisps(101,1).Value); Assert.Equal(1,Map2320NavigationMechanics.FutureCraftWisps(101,3).Value); Assert.Equal(0,Map2320NavigationMechanics.FutureCraftWisps(101,2).Value);
    }
    [Theory] [InlineData(9,Map2320Status.Bounded)] [InlineData(10,Map2320Status.Ineligible)]
    public void RerollBoundary(int round,Map2320Status expected) => Assert.Equal(expected,Map2320NavigationMechanics.QuestReroll(round,0,2,false,false,true));
    [Fact] public void MissionGuards()
    {
        Assert.Equal(Map2320Status.Ineligible,Map2320NavigationMechanics.QuestReroll(1,0,0,true,false,true)); Assert.Equal(Map2320Status.Ineligible,Map2320NavigationMechanics.QuestReroll(1,0,0,false,true,true));
        Assert.Equal(Map2320Status.Bounded,Map2320NavigationMechanics.Demolition(8,true)); Assert.Equal(Map2320Status.Ineligible,Map2320NavigationMechanics.Demolition(9,true));
        Assert.Equal(Map2320Status.Ineligible,Map2320NavigationMechanics.MysteryQuestPool(Map2320Mode.Normal)); Assert.Equal(Map2320Status.Bounded,Map2320NavigationMechanics.MysteryQuestPool(Map2320Mode.Otherworld));
    }
    [Fact] public void UnknownModeWaveStateAndUnmigratedScoresNeverRecommend()
    {
        var data=Map2320NavigationMechanics.Load(DataFile);
        foreach(var c in new[]{new Map2320NavigationContext(Map2320Mode.Unknown,1,0,true),new(Map2320Mode.Normal,null,0,true),new(Map2320Mode.Otherworld,5,0,false),new(Map2320Mode.Otherworld,5,0,true)}) {
            var advice=Map2320NavigationAdvisor.Advise(data,c); Assert.Null(advice.RecommendedOptionId); Assert.Equal(15,advice.Options.Count);Assert.All(advice.Options,o=>Assert.False(o.IsSafeRecommendation));
        }
        Assert.Null(Map2320NavigationMechanics.ResolveIdentity("2.314","Gambler.ContinuousBetting")); Assert.Null(Map2320NavigationMechanics.ResolveIdentity("2.320","Gambler.ContinuousBetting")); Assert.Equal("Gambler.Exchange2320",Map2320NavigationMechanics.ResolveIdentity("2.320","Gambler.Exchange2320"));
    }
}
