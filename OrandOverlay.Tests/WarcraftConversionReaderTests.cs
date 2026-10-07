using Xunit;
namespace OrandOverlay.Tests;
public sealed class WarcraftConversionReaderTests
{
    [Fact]
    public void ReadsPinnedLocalSaleAbilityThroughExistingValidatedChain()
    {
        var f = new Memory();
        var state = f.Read();
        Assert.NotNull(state);
        Assert.Equal(new HelperAbilityState("A0B8", 1, 0), state.UncommonSaleAbility);
    }
    [Fact]
    public void ReadsAncientShipAbilityWithoutTreatingRayleighGambleAsGuaranteed()
    {
        var f = new Memory { Code = 0x68303559 };
        f.U32(Memory.Unit+0x178,f.Code); f.U32(Memory.Ability+0x70,0x41304b42);
        Assert.Equal(new HelperAbilityState("A0KB",1,0),f.Read()?.AncientShipAbility);
    }
    [Theory]
    [InlineData("version")][InlineData("map")][InlineData("generation")][InlineData("owner")]
    [InlineData("getter")][InlineData("level")][InlineData("cycle")][InlineData("h00B")][InlineData("h00H")]
    [InlineData("shipOnHelper")][InlineData("gamble")][InlineData("doubleReadRace")]
    public void InvalidOrUnjoinedNativeStateCannotBecomeReady(string blocked)
    {
        var f=new Memory();
        if(blocked=="generation") f.U32(0x140024,10);
        if(blocked=="owner") f.Put(Memory.Unit+0x1c0,[1]);
        if(blocked=="getter") f.U64(0x402648,0);
        if(blocked=="level") f.U32(Memory.Ability+0x9c,1);
        if(blocked=="cycle") f.U64(Memory.Ability+0x58,9UL<<32);
        if(blocked is "h00B" or "h00H" or "shipOnHelper" or "gamble")
        {
            f.Code=blocked switch { "h00B"=>0x68303042, "h00H"=>0x68303048, "shipOnHelper"=>0x68303841, _=>0x68303559 };
            f.U32(Memory.Unit+0x178,f.Code);
            if(blocked=="shipOnHelper") f.U32(Memory.Ability+0x70,0x41304b42);
            if(blocked=="gamble") f.U32(Memory.Ability+0x70,0x41304b43);
        }
        var reads=0;
        byte[] Read(ulong a,int n)
        {
            if(a==Memory.Ability+0x9c && ++reads==2) f.U32(a,1);
            return Enumerable.Range(0,n).Select(i=>f.Bytes.GetValueOrDefault(a+(ulong)i)).ToArray();
        }
        var result=f.Read(blocked=="version"?"unknown":"2.0.4.23745",blocked=="map"?"wrong":null,
            blocked=="doubleReadRace"?Read:null);
        Assert.False(result?.UncommonSaleAbility is { Level:1,CooldownRemaining:0 });
        Assert.Null(result?.AncientShipAbility);
    }
    [Fact]
    public void NewAbilityFieldsParticipateInRecordEqualityAndRefreshText()
    {
        var first=new Memory().Read()!;
        var changed=first with { UncommonSaleAbility=new("A0B8",1,null) };
        Assert.NotEqual(first,changed);
        Assert.NotEqual(first.ToString(),changed.ToString());
        Assert.NotEqual(first.ToString(),(first with { AncientShipAbility=new("A0KB",1,0) }).ToString());
    }
    internal sealed class Memory
    {
        internal readonly Dictionary<ulong, byte> Bytes = new();
        internal const ulong Unit = 0x100000, Module = 0x400000, Ability = 0x180000;
        internal uint Code = 0x68303041;
        internal Memory()
        {
            U64(Unit, 0x401000); U64(0x401178, Module+0x1163ad0); U64(0x401278, Module+0x1163a00); U64(0x401280, Module+0x1163a20);
            U32(Unit+0x178, Code); U64(Unit+0x258, ulong.MaxValue); U64(Unit+0x3b8, ulong.MaxValue);
            U64(Unit+0x558, 9UL<<32); U64(Module+0x2b808c0,0x120000); U32(0x120030,1); U64(0x120018,0x130000);
            U32(0x130000,0xfffffffe); U64(0x130008,0x140000); U32(0x140024,9); U64(0x140090,Ability);
            U32(Ability+0x70,0x41304238); U64(Ability+0x58,ulong.MaxValue);
            U64(Ability,0x402000); U64(0x402648,Module+0x6515d0);
        }
        internal void U32(ulong a,uint v) => Put(a,BitConverter.GetBytes(v));
        internal void U64(ulong a,ulong v) => Put(a,BitConverter.GetBytes(v));
        internal void Put(ulong a,byte[] v) { for(var i=0;i<v.Length;i++) Bytes[a+(ulong)i]=v[i]; }
        internal CombatUnitState? Read(string version="2.0.4.23745",string? hash=null,Func<ulong,int,byte[]>? read=null) =>
            new WarcraftCombatReader(read ?? ((a,n)=>Enumerable.Range(0,n).Select(i=>Bytes.GetValueOrDefault(a+(ulong)i)).ToArray()),Module,0x4000000)
                .Read(version,hash ?? RouteQuestCatalog.MapScriptSha256,Unit,Code,0,0,1);
    }
}
