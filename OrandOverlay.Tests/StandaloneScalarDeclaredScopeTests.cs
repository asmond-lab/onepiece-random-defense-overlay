using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;
public sealed class StandaloneScalarDeclaredScopeTests
{
    [Fact] public void PinnedCatalogResolvesExactUnionAndUnconsumedMismatchWithoutValueInterpretation()
    {
        var scope=PreludeDeclarationCatalog.Load(Map2320GrowthSource.LoadBundled().Globals);
        Assert.Equal(6196,scope.Declarations.Count); Assert.Equal(3936,scope.Declarations.Count(p=>p.Value.IsMap));
        Assert.Equal(PreludeDeclarationCatalog.UnionFingerprint,scope.Fingerprint);
        scope.Validate("bj_stockPickedItemType",0,7);
        Assert.Throws<InvalidDataException>(()=>scope.ValidateConsumed("bj_stockPickedItemType",0,7));
        Assert.Throws<InvalidDataException>(()=>scope.Validate("bj_stockPickedItemType",14,7));
        Assert.Throws<InvalidDataException>(()=>scope.Validate("bj_stockPickedItemType",0,8));
        foreach(var name in new[]{"pb","Eb","qg","QR","FALSE"})
            Assert.Throws<InvalidDataException>(()=>scope.Validate(name,0,(uint)scope.Declarations[name].Tag));
        Assert.Throws<InvalidDataException>(()=>scope.Validate("not_a_source_declaration",7,7));
        Assert.Throws<InvalidDataException>(()=>scope.Validate("BJ_STOCKPICKEDITEMTYPE",7,7));
    }
    [Fact] public void InheritanceHasNoFallbackAndRejectsCyclesAndCodeArrays()
    {
        var types=new Dictionary<string,string>{{"itemtype","agent"},{"agent","handle"}};
        Assert.Equal(7,PreludeDeclarationCatalog.ResolveTag("itemtype",false,types));
        Assert.Equal(12,PreludeDeclarationCatalog.ResolveTag("itemtype",true,types));
        Assert.Throws<InvalidDataException>(()=>PreludeDeclarationCatalog.ResolveTag("ItemType",false,types));
        Assert.Throws<InvalidDataException>(()=>PreludeDeclarationCatalog.ResolveTag("code",true,types));
        types["agent"]="itemtype";
        Assert.Throws<InvalidDataException>(()=>PreludeDeclarationCatalog.ResolveTag("itemtype",false,types));
    }
    [Fact] public void ResourceHashAndUnionCollisionsAreRejectedBeforeUse()
    {
        Assert.Throws<InvalidDataException>(()=>PreludeDeclarationCatalog.Parse(new byte[]{1},new Dictionary<string,int>()));
        var map=new Dictionary<string,int>{{"QR",12}};
        Assert.Throws<InvalidDataException>(()=>new Warcraft300DeclaredScope(map,new[]{new KeyValuePair<string,Warcraft300Declaration>("QR",new(7,false,false))},"pin"));
        var dup=new KeyValuePair<string,Warcraft300Declaration>("other",new(7,false,false));
        Assert.Throws<InvalidDataException>(()=>new Warcraft300DeclaredScope(map,new[]{dup,dup},"pin"));
    }
    private static Warcraft300SnapshotNode Node(string name,ulong a,ulong pointer,uint runtime=7,uint declared=7)
    {
        var m=new byte[32]; BitConverter.GetBytes(pointer).CopyTo(m,16); BitConverter.GetBytes(runtime).CopyTo(m,24); BitConverter.GetBytes(declared).CopyTo(m,28);
        return new(name,a,m);
    }
    [Fact] public void NodeClonesCallerBytesAndReplaysOnlyExactOpeningNameAndMetadata()
    {
        var m=new byte[32]; BitConverter.GetBytes(0x30000UL).CopyTo(m,16); BitConverter.GetBytes(0U).CopyTo(m,24); BitConverter.GetBytes(7U).CopyTo(m,28);
        var original=m.ToArray(); var node=new Warcraft300SnapshotNode("other",0x20000,m); m[16]^=8;
        var calls=new List<(ulong,int)>();
        node.Recheck((a,n)=>{calls.Add((a,n));return a==0x20018?original:Encoding.ASCII.GetBytes("other\0");});
        Assert.Equal(new[]{(0x20018UL,32),(0x30000UL,6)},calls);
        Assert.Throws<InvalidDataException>(()=>node.Recheck((a,n)=>a==0x20018?m:throw new Exception("Must not follow changed pointer")));
        Assert.Throws<InvalidDataException>(()=>node.Recheck((a,n)=>a==0x20018?original:Encoding.ASCII.GetBytes("Other\0")));
        var changed=original.ToArray(); changed[24]=1;
        Assert.Throws<InvalidDataException>(()=>node.Recheck((a,n)=>a==0x20018?changed:throw new Exception()));
    }
    [Fact] public void SnapshotRejectsMissingDuplicateAndOverlappingNodesAndClonesHeaders()
    {
        var scope=new Warcraft300DeclaredScope(new Dictionary<string,int>{{"QR",12}},new[]{new KeyValuePair<string,Warcraft300Declaration>("other",new(7,false,false))},"p");
        var a=Node("QR",0x20000,0x30000,12,12); var b=Node("other",0x20100,0x30100,0,7);
        var owner=new byte[24]; var table=new byte[72]; var context=new object();
        Warcraft300GlobalsSnapshot Make(params Warcraft300SnapshotNode[] nodes)=>new(Stopwatch.GetTimestamp(),"s",0x140000000,0x40000,new(0x50000,0,0x60000),scope,nodes,owner,table,context,0x70000,0x80000,0x20000);
        Assert.Throws<InvalidDataException>(()=>Make(a)); Assert.Throws<InvalidDataException>(()=>Make(a,a));
        Assert.Throws<InvalidDataException>(()=>Make(a,Node("other",0x20020,0x30100,0,7)));
        var first=Make(a,b); var second=Make(a,b); Assert.NotEqual(first.Invocation,second.Invocation);
        owner[0]=1; table[0]=1; Assert.Equal(0,first.OwnerHeaderCopy()[0]); Assert.Equal(0,first.TableHeaderCopy()[0]);
        var exposed=first.OwnerHeaderCopy(); exposed[1]=1; Assert.Equal(0,first.OwnerHeaderCopy()[1]);
    }
    [Fact] public void OneFullScopeReplayFitsButSecondCannotAndBudgetsNeverReset()
    {
        var budget=new BoundReadSession.ProbeBudget(()=>TimeSpan.FromSeconds(1));
        budget.Charge(857600); budget.Charge(268791); budget.Charge(857600);
        Assert.Equal(1983991,budget.Snapshot().RequestedReadBytes);
        budget.Charge(100000); Assert.Throws<InvalidDataException>(()=>budget.Charge(268791));
        Assert.Equal(2083991,budget.Snapshot().RequestedReadBytes);
    }
}
