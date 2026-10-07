using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
namespace OrandOverlay.Tests;
public class Map2320StoryProfileTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory,"Data","story-progression-2320.json");
    [Fact] public void LoadsFourteenOrderedObjectivesAndProjectsOnlyGuaranteedBase()
    {
        var p=Map2320StoryProfile.LoadBundled();
        Assert.Equal(new[]{"n000","n002","n003","n004","n005","n006","n007","n008","n00A","n001","n00C","n00D","n00B","n009"},p.Stages.Select(s=>s.ObjectiveRawcode));
        Assert.Equal(Enumerable.Range(1,14),p.Stages.Select(s=>s.Ordinal));
        var projected=p.ProjectGuaranteedBaseStages();
        Assert.Equal(14,projected.Length);
        Assert.All(projected,s=>{ Assert.Empty(s.RewardComponents.Mvp); Assert.Empty(s.RewardComponents.ContributionAtLeast25Percent); });
        Assert.All(p.Stages.Take(3),s=>{Assert.Empty(s.ContributionQualified);Assert.Contains(s.EveryPlayerBase,r=>r.Id=="e0IX"&&r.Amount==1);Assert.Contains(s.Mvp,r=>r.Kind=="Gold");});
        Assert.Contains(projected[9].RewardComponents.EveryPlayerBase,r=>r.Id=="e01A");
        Assert.DoesNotContain(projected.SelectMany(s=>s.RewardComponents.EveryPlayerBase),r=>r.Id=="e0IA");
        Assert.All(p.Stages,s=>Assert.All(s.EveryPlayerBase.Concat(s.ContributionQualified).Concat(s.Mvp).Concat(s.HiddenOrSideEffect),r=>Assert.NotEmpty(r.Sources)));
    }
    [Theory] [InlineData(0,20)] [InlineData(1,20)] [InlineData(2,30)] [InlineData(3,25)] [InlineData(4,20)]
    public void CountsOnlyActivePlayingHumansAtCompletion(int count,int threshold)
    {
        var s=Map2320StoryProfile.LoadBundled().Stages[3];
        var players=Enumerable.Range(0,4).Select(i=>new Map2320PlayerAtCompletion(i<count,true,true)).ToArray();
        Assert.Equal(Map2320QualificationResult.Qualified,s.Qualify(true,threshold,100,players));
        Assert.Equal(Map2320QualificationResult.NotQualified,s.Qualify(true,threshold-.01,100,players));
        players[0]=new(true,false,true);
        Assert.Equal(Map2320QualificationResult.Unknown,s.Qualify(true,null,100,players));
        Assert.Equal(Map2320QualificationResult.Unknown,s.Qualify(true,25,double.NaN,players));
        Assert.Equal(Map2320QualificationResult.Unknown,s.Qualify(true,25,0,players));
        Assert.Equal(Map2320QualificationResult.NotQualified,s.Qualify(false,null,null,null));
    }
    [Fact] public void FirstThreeUnconditionalButMvpIsSeparateAndUnknownIsFailClosed()
    {
        var p=Map2320StoryProfile.LoadBundled();
        Assert.All(p.Stages.Take(3),s=>Assert.Equal(Map2320QualificationResult.Qualified,s.Qualify(true,null,null,null)));
        Assert.Equal(Map2320QualificationResult.Unknown,p.Stages[3].Qualify(true,99,100,null));
        var unknown=p with { Stages=p.Stages.SetItem(0,p.Stages[0] with { EveryPlayerBase=p.Stages[0].EveryPlayerBase.Select(r=>r with { Known=false }).ToImmutableArray() }) };
        Assert.Empty(unknown.ProjectGuaranteedBaseStages()[0].RewardComponents.EveryPlayerBase);
    }
    [Theory] [InlineData("Amount")] [InlineData("Id")] [InlineData("Condition")] [InlineData("ChanceDenominator")] [InlineData("Limit")] [InlineData("Known")]
    public void RejectsRewardMutationsInEveryGroup(string field)
    {
        foreach(var group in new[]{"EveryPlayerBase","ContributionQualified","Mvp","HiddenOrSideEffect"})
        {
            var j=JsonNode.Parse(File.ReadAllText(Data))!;var rewards=j["Stages"]![4]![group]!;var r=rewards[0]!;
            r[field]=field switch {"Id"=>JsonValue.Create("e0IA"),"Condition"=>JsonValue.Create("unknown"),"Known"=>JsonValue.Create(false),_=>JsonValue.Create(-1)};
            Assert.Throws<InvalidDataException>(()=>Map2320StoryProfile.Load(Encoding.UTF8.GetBytes(j.ToJsonString())));
        }
    }
    [Fact] public void RejectsDuplicateUnknownMissingFieldsAndSourcePins()
    {
        var text=File.ReadAllText(Data);
        Assert.Throws<InvalidDataException>(()=>Map2320StoryProfile.Load(Encoding.UTF8.GetBytes(text.Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 1, \"SchemaVersion\": 1"))));
        Assert.Throws<InvalidDataException>(()=>Map2320StoryProfile.Load(Encoding.UTF8.GetBytes(text.Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 1, \"Unknown\": 0"))));
        Assert.Throws<InvalidDataException>(()=>Map2320StoryProfile.Load(Encoding.UTF8.GetBytes(text.Replace("\"SchemaVersion\": 1,",""))));
        var p=Map2320StoryProfile.LoadBundled(); var stage=p.Stages[0]; var r=stage.EveryPlayerBase[0];
        Assert.Throws<InvalidDataException>(()=>Map2320StoryProfile.Validate(p with { Stages=p.Stages.SetItem(0,stage with { EveryPlayerBase=stage.EveryPlayerBase.SetItem(0,r with {Amount=181}) }) }));
        Assert.Throws<InvalidDataException>(()=>Map2320StoryProfile.Validate(p with { Stages=p.Stages.SetItem(0,stage with { EveryPlayerBase=stage.EveryPlayerBase.SetItem(0,r with {Sources=r.Sources.SetItem(0,r.Sources[0] with {EvidenceLine=1})}) }) }));
    }
}
