using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Map2320NavigationData(int SchemaVersion, string MapVersion, Map2320SourcePin[] SourcePins, Map2320NavigationOption[] Options, Map2320ExchangeDefinition[] Exchanges, Map2320NavigationEvidence[] Evidence, string[] Limitations);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Map2320SourcePin(string Path, long Length, string Sha256);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Map2320NavigationOption(string Id, string Name, string Category, string[] Effects, string ScoringStatus, string[] SourceFunctions);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Map2320ExchangeDefinition(string Ability, int Cost, string Function, string Outcome, double CooldownSeconds, int? ManaCost, string ManaStatus, string ProbabilityStatus, bool DisableAfterUse, string LifetimeOnceStatus);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Map2320NavigationEvidence(string Function, int StartLine, int EndLine, string Text);
public enum Map2320Mode { Unknown, Normal, Otherworld }
public enum Map2320Status { Bounded, Unknown, Ineligible, InsufficientPoints, Disabled, Overflow, InvalidInput }
public sealed record Map2320Reward(string Kind, int Minimum, int Maximum, string[]? Rawcodes = null);
public sealed record Map2320ExchangeState(int Points, int NormalStacks, int UniqueStacks, bool? AbilityEnabled, Map2320Mode Mode);
public sealed record Map2320ExchangeResult(Map2320Status Status, int? PointsAfter, int? StacksAfter, bool DisableAfterUse, IReadOnlyList<Map2320Reward> Alternatives, string Detail)
{
    public string ProbabilityStatus => "Unknown";
    public bool IsSafeRecommendation => false;
}
public sealed record Map2320IntegerResult(Map2320Status Status, int? Value);
public sealed record Map2320WorldResult(Map2320Status Status, int? Remaining, int? Consumed, int? Risk, long? SuccessThreshold, string Outcome);
public sealed record Map2320CraftCounters(int Dg, int Mg, int Vg, int CapitalDg, int Eg, int Gg, int Xg, int Ag, int Pg);
public sealed record Map2320Leo(int AbilityLevel, int ConfiguredSlowPercent, int AuraRadius, int TopUnitLimit, int AttackDamage, int AttackRadius, int NextCycle, bool StunStage);

/// <summary>Offline, version-scoped rules. Bounded outcomes never imply RNG probabilities, cast success, or live support.</summary>
public static class Map2320NavigationMechanics
{
    public const string MapVersion = "2.320";
    public const string DataSha256 = "85cdf15050e01d6069aabb44cea542fef4d13ca83292fb4eeab0d05566969283";
    public const string JassSha256 = "6fdfc64bf8ad9463f5b5c8a351ffa7e1875d6cf9b51210129539375fa77a6c7c";
    public const string ExchangeOptionId = "Gambler.Exchange2320";
    private static readonly Dictionary<string,int> Costs = new(StringComparer.Ordinal) { ["A0CI"]=1,["A0EZ"]=3,["A0DB"]=3,["A0IY"]=5,["A0JR"]=3,["A0BV"]=10,["A0MA"]=20 };
    private static readonly string[] KnownIds = { "AlliedForces.DoubleBenefit","AlliedForces.EmergencyCall","AlliedForces.TraitEngineering","PathOfKings.MartialLaw","PathOfKings.BountyHunter","PathOfKings.RoyalLoader","Gambler.Casino","Gambler.RiskHedge","Gambler.Exchange2320","BestHelp.MaximumOutput","BestHelp.Alchemy","BestHelp.ReverseThinking","Random.BlueFlavor","Random.GreenFlavor","Random.YellowFlavor" };
    private static readonly string[] WorldPool = { "h06U","h06V","h070","h06T","h09L","h06Z","h073","h09K","h06Y","h071","h072","h06W","h065","h06X" };
    public static Map2320NavigationData Load(string file) => Load(File.ReadAllBytes(file));
    public static Map2320NavigationData Load(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= 0 || bytes.Length > 512 * 1024) throw new InvalidDataException("Navigation data size bound.");
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(DataSha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("2.320 navigation data hash mismatch.");
        var d=JsonSerializer.Deserialize<Map2320NavigationData>(bytes,new JsonSerializerOptions { PropertyNameCaseInsensitive=false, UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing navigation data.");
        if(d.SchemaVersion!=1 || d.MapVersion!=MapVersion || d.Options.Length!=15 || d.Options.Select(x=>x.Id).Distinct().Count()!=15 || d.Exchanges.Length!=7 || d.SourcePins[0].Sha256!=JassSha256) throw new InvalidDataException("Invalid 2.320 navigation schema.");
        return d;
    }
    public static void VerifySources(Map2320NavigationData data, string repositoryRoot)
    {
        foreach(var pin in data.SourcePins)
        {
            var bytes=File.ReadAllBytes(System.IO.Path.Combine(repositoryRoot,pin.Path));
            if(bytes.LongLength!=pin.Length || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(pin.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Source mismatch: "+pin.Path);
        }
    }
    // Legacy ContinuousBetting remains a different identity. No global alias is offered.
    public static string? ResolveIdentity(string version, string id) => version==MapVersion && KnownIds.Contains(id, StringComparer.Ordinal) ? id : null;
    public static Map2320IntegerResult IntermediateAttempt(int points, bool? attempted, bool? succeeded)
    {
        if(points<0) return new(Map2320Status.InvalidInput,null);
        if(attempted is null) return new(Map2320Status.Unknown,null);
        if(!attempted.Value) return new(Map2320Status.Ineligible,points);
        // Ejy grants before outcome. succeeded intentionally does not gate the credit.
        return Checked((long)points+1);
    }
    private static Map2320IntegerResult Checked(long n) => n<0 || n>int.MaxValue ? new(Map2320Status.Overflow,null) : new(Map2320Status.Bounded,(int)n);
    public static Map2320ExchangeResult Exchange(string ability, Map2320ExchangeState state)
    {
        Map2320ExchangeResult Reject(Map2320Status s,string why)=>new(s,null,null,false,Array.Empty<Map2320Reward>(),why);
        if(!Costs.TryGetValue(ability,out var cost) || state.Points<0 || state.NormalStacks<0 || state.NormalStacks>10 || state.UniqueStacks<0 || state.UniqueStacks>10 || !Enum.IsDefined(state.Mode)) return Reject(Map2320Status.InvalidInput,"Invalid state or ability.");
        if(state.AbilityEnabled is null || state.Mode==Map2320Mode.Unknown) return Reject(Map2320Status.Unknown,"Mode and ability availability must be known.");
        if(!state.AbilityEnabled.Value) return Reject(Map2320Status.Disabled,"Ability currently disabled; lifetime re-enable semantics unknown.");
        if(ability=="A0MA" && state.Mode!=Map2320Mode.Otherworld) return Reject(Map2320Status.Ineligible,"Otherworld only.");
        if(state.Points<cost) return Reject(Map2320Status.InsufficientPoints,"No debit or grant.");
        int? stack=null; bool disable=ability is "A0JR" or "A0BV";
        Map2320Reward[] rewards=ability switch {
            "A0CI"=>new[]{new Map2320Reward("Gold",5000,5000),new Map2320Reward("RandomWisp",1,1),new Map2320Reward("SelectableWisp",1,1)},
            "A0EZ"=>new[]{new Map2320Reward("Lumber",1,2)},
            "A0DB"=>new[]{new Map2320Reward("SelectableWisp",1,2)},
            "A0IY"=>new[]{new Map2320Reward("Ship",1,1,new[]{"h060"})},
            "A0MA"=>new[]{new Map2320Reward("WorldUnitWithA0YZ",1,1,(string[])WorldPool.Clone()),new Map2320Reward("AlsoToken",1,1,new[]{"h06G"})},
            _=>Array.Empty<Map2320Reward>() };
        if(disable) { var before=ability=="A0JR"?state.NormalStacks:state.UniqueStacks; stack=Math.Min(10,before+1); rewards=new[]{new Map2320Reward(ability=="A0JR"?"NormalExcavation":"UniqueExcavation",stack.Value-before,stack.Value-before)}; }
        return new(Map2320Status.Bounded,state.Points-cost,stack,disable,rewards,"Conditional on reaching handler. Debit precedes cap; no refund. Mana inherited ANcl unknown; cooldown approximately0.2s. A0CI entries are alternatives; A0MA token is additional.");
    }
    public static Map2320IntegerResult CasinoSelection(int remaining,int consumed,Map2320Mode mode)
    {
        if(remaining<0 || consumed<0 || !Enum.IsDefined(mode)) return new(Map2320Status.InvalidInput,null);
        return mode==Map2320Mode.Unknown?new(Map2320Status.Unknown,null):mode==Map2320Mode.Normal?new(Map2320Status.Ineligible,remaining):Checked(2L*remaining+consumed);
    }
    public static Map2320IntegerResult FutureWorldGrant(int remaining,bool casino,int requested=1) => remaining<0 || requested<0 ? new(Map2320Status.InvalidInput,null) : Checked((long)remaining+requested+(casino?1:0));
    public static Map2320WorldResult WorldAttempt(int remaining,int consumed,int risk,bool riskHedge,bool casino,Map2320Mode mode,int? roll)
    {
        if(remaining<0 || consumed<0 || risk<0 || !Enum.IsDefined(mode) || roll is <1 or >100 || (riskHedge&&casino)) return new(Map2320Status.InvalidInput,null,null,null,null,"Invalid state");
        long threshold=15L+(riskHedge?risk:0);
        if(mode==Map2320Mode.Unknown || roll is null) return new(Map2320Status.Unknown,null,null,null,threshold,"Unknown outcome/RNG; no safe recommendation");
        if(mode!=Map2320Mode.Otherworld || remaining==0) return new(Map2320Status.Ineligible,null,null,null,threshold,"No eligible attempt");
        bool success=roll.Value<=threshold;
        long nextRisk=success?0L:(long)risk+(riskHedge?15:0);
        if(consumed==int.MaxValue || nextRisk>int.MaxValue) return new(Map2320Status.Overflow,null,null,null,threshold,"Arithmetic overflow");
        return new(Map2320Status.Bounded,remaining-1,consumed+1,(int)nextRisk,threshold,success?"xx[0..13] with A0YZ; identity probability unknown":casino?"ix[0..41] with A0YE OR h06G; conditional roll distribution unknown":"h06G");
    }
    public static Map2320Leo Leo(bool martial,int cycle=0)
    {
        if(cycle<0 || cycle>3) throw new ArgumentOutOfRangeException(nameof(cycle));
        return martial?new(2,20,850,0,cycle==3?1200000:500000,650,(cycle+1)%4,cycle==3):new(1,10,850,1,300000,550,0,false);
    }
    public static Map2320Status PathSelection(bool martial,int? topUnits) => topUnits is null?Map2320Status.Unknown:topUnits<0?Map2320Status.InvalidInput:topUnits>(martial?0:1)?Map2320Status.Ineligible:Map2320Status.Bounded;
    public static Map2320IntegerResult DoubleBenefitRetroactive(Map2320CraftCounters? counters)
    {
        if(counters is null) return new(Map2320Status.Unknown,null);
        int[] c={counters.Dg,counters.Mg,counters.Vg,counters.CapitalDg,counters.Eg,counters.Gg,counters.Xg,counters.Ag,counters.Pg};
        return c.Any(x=>x<0)?new(Map2320Status.InvalidInput,null):Checked(c.Sum(x=>(long)x));
    }
    public static Map2320IntegerResult FutureCraftWisps(int? pointValue,int? alliedOption) => pointValue is null || alliedOption is null?new(Map2320Status.Unknown,null):pointValue<0 || alliedOption<0 || alliedOption>3?new(Map2320Status.InvalidInput,null):new(Map2320Status.Bounded,pointValue<=100 || alliedOption==0 || alliedOption==2?0:alliedOption==1?2:1);
    public static Map2320Status QuestReroll(int? round,int player,int slot,bool? used,bool? completed,bool? nonempty)
    {
        if(round is null || used is null || completed is null || nonempty is null) return Map2320Status.Unknown;
        if(round<0 || player<0 || player>=4 || slot<0 || slot>=3) return Map2320Status.InvalidInput;
        return round<10 && !used.Value && !completed.Value && nonempty.Value?Map2320Status.Bounded:Map2320Status.Ineligible;
    }
    public static Map2320Status MysteryQuestPool(Map2320Mode selectedMode) => selectedMode==Map2320Mode.Unknown?Map2320Status.Unknown:selectedMode==Map2320Mode.Otherworld?Map2320Status.Bounded:Map2320Status.Ineligible;
    public static Map2320Status Demolition(int? round,bool? pending) => round is null || pending is null?Map2320Status.Unknown:round<0?Map2320Status.InvalidInput:round<9 && pending.Value?Map2320Status.Bounded:Map2320Status.Ineligible;
}
