using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using Xunit;

namespace OrandOverlay.Tests;
public sealed class Warcraft300ObservedRoundReaderTests
{
    private const ulong Pb=0xB00000,Eb=0xB10000,Qg=0xB20000,PbName=0xB30000,EbName=0xB40000,QgName=0xB50000,
        Dialog=0xC00000,DialogUi=0xC10000,Timer=0xC20000,Frame=0xC30000,DialogName=0xD00000,FrameName=0xD10000,Title=0xD20000,
        DialogRecord=0xE00000,TimerRecord=0xE10000;
    private static Memory Setup(int round=1)
    {
        var m=new Memory();
        foreach(var a in new[]{Pb,Eb,Qg,PbName,EbName,QgName,Dialog,DialogUi,Timer,Frame,DialogName,FrameName,Title,DialogRecord,TimerRecord}) m.Allocate(a,0x3000);
        var nodes=new[]{(Pb,PbName,"pb",4U),(Eb,EbName,"Eb",4U),(Qg,QgName,"qg",7U)};
        m.Put64(Memory.Extra+32,Pb); m.Put64(Memory.Table+16,Qg+24);
        for(int i=0;i<3;i++)
        {
            var n=nodes[i]; m.Put64(n.Item1+24,i==0?Memory.Extra+24:nodes[i-1].Item1+24);
            m.Put64(n.Item1+32,i==2?Memory.Table+17:nodes[i+1].Item1); m.Put64(n.Item1+40,n.Item2);
            m.Put32(n.Item1+48,n.Item4); m.Put32(n.Item1+52,n.Item4); m.Name(n.Item2,n.Item3); m.Expected[n.Item3]=(int)n.Item4;
        }
        m.Put32(Pb+56,(uint)round); m.Put32(Eb+56,(uint)round+1); m.Put32(Qg+56,0x100001);
        m.Put32(Memory.Manager+0x290,2); m.Put32(Memory.JassTable+24,1); m.Put64(Memory.JassTable+32,Dialog);
        m.Put64(Dialog,Memory.Module+0x2730B08); m.Put64(Dialog+0x18,0x600000001); m.Put64(Dialog+0x58,DialogUi); m.Put64(Dialog+0x60,0x700000002);
        m.Put64(DialogUi,Memory.Module+0x2768190); m.Put64(DialogUi+0x40,Memory.Ui); m.Put64(DialogUi+0x280,DialogName);
        m.Put64(DialogUi+0x2D8,Timer); m.Put64(DialogUi+0x298,Frame); m.Name(DialogName,"TimerDialog");
        m.Put64(Timer,Memory.Module+0x26E04E0); m.Put64(Timer+0x18,0x700000002);
        m.Put64(Frame,Memory.Module+0x228F5D0); m.Put64(Frame+0x40,DialogUi); m.Put64(Frame+0x280,FrameName); m.Name(FrameName,"TimerDialogTitle");
        m.Put64(Frame+0x4C8,Title); m.Put64(Frame+0x4D0,Title);
        TitleText(m,round==0?"|cffFF0000 1라운드 시작까지|r":$"|cffFF0000현재 라운드|r : {round}|r");
        m.Put32(Memory.Registry+0x30,3);
        foreach(var n in new[]{(1UL,6U,DialogRecord,Dialog),(2UL,7U,TimerRecord,Timer)})
        { m.Put32(Memory.NativeTable+n.Item1*16,0xFFFFFFFE); m.Put64(Memory.NativeTable+n.Item1*16+8,n.Item3);
          m.Put32(n.Item3+0x18,0x2B61676C); m.Put32(n.Item3+0x24,n.Item2); m.Put64(n.Item3+0x90,n.Item4); }
        return m;
    }
    private static void TitleText(Memory m,string text) { m.Put(Title,new byte[96]); m.Put(Title,Encoding.UTF8.GetBytes(text+"\0")); }
    private static Warcraft300ObservedRoundReader.Context Context(Memory m)=>new(m.Session,Memory.Module,m.View,Memory.World);
    private static Warcraft300ObservedRoundReader.Observation Round(Memory m,Warcraft300GrowthObservation g,CancellationToken token=default)=>
        Warcraft300ObservedRoundReader.Read(m.Bytes,g,Memory.Module,_=>Context(m),token);
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(65)]
    public void CoherentPairAndOwnedTitleOnlyReturnsReferenceRound(int round)
    {
        var m=Setup(round); var growth=m.Read(); var result=Round(m,growth);
        Assert.Equal(round,result.Round); Assert.Equal(round,result.Pb); Assert.Equal(round+1,result.Eb); Assert.True(result.SuccessfulComparison);
        Assert.False(result.CanCoach); Assert.False(result.LayoutVerified); Assert.False(result.GameplayReady);
        Assert.InRange(result.RequestedReadBytes,1,16*1024); Assert.InRange(result.ReadCalls,1,512); Assert.Equal(Memory.Unit,growth.UnitPointer);
    }
    [Fact] public void NewGrowthCallUsesNewSelectedInputsForRoundTransition()
    {
        var m=Setup(); var first=m.Read(); Assert.Equal(1,Round(m,first).Round);
        m.Put32(Pb+56,2); m.Put32(Eb+56,3); TitleText(m,"|cffFF0000현재 라운드|r : 2|r");
        var next=m.Read(); Assert.NotSame(first.RoundInputs,next.RoundInputs); Assert.NotEqual(first.RoundInputs!.Invocation,next.RoundInputs!.Invocation);
        Assert.Equal(2,Round(m,next).Round); Assert.Equal(2,m.Enumerations);
    }
    [Fact] public void PreroundHasNoPositiveRoundAndNoApproval()
    { var m=Setup(0); var r=Round(m,m.Read()); Assert.Null(r.Round); Assert.Equal("PreRound",r.Status); Assert.False(r.SuccessfulComparison); }
    [Theory] [InlineData(-1,0)] [InlineData(66,67)] [InlineData(int.MaxValue,int.MinValue)] [InlineData(1,3)] [InlineData(0,0)]
    public void InvalidPairsNeverClampOrGuess(int pb,int eb)
    { var m=Setup(); m.Put(Pb+56,BitConverter.GetBytes(pb)); m.Put(Eb+56,BitConverter.GetBytes(eb)); Assert.Null(Round(m,m.Read()).Round); }
    [Fact] public void TitleMismatchDoesNotChangeUnitAssociation()
    { var m=Setup(); var g=m.Read(); TitleText(m,"|cffFF0000현재 라운드|r : 2|r"); Assert.Null(Round(m,g).Round); Assert.Equal(Memory.Unit,g.UnitPointer); }
    [Theory] [InlineData(Pb+48)] [InlineData(PbName)] [InlineData(Qg+40)] [InlineData(DialogUi+0x40)]
    [InlineData(Frame+0x4D0)] [InlineData(DialogRecord+0x18)] [InlineData(DialogRecord+0x24)]
    [InlineData(TimerRecord+0x30)] [InlineData(TimerRecord+0x90)] [InlineData(Memory.NativeTable+16)]
    [InlineData(Memory.Table+16)] [InlineData(Memory.Instance+8)] [InlineData(FrameName)]
    public void MutationOfSelectedIdentityOwnershipOrNativeAllocationIsUnavailable(ulong address)
    {
        var m=Setup(); var g=m.Read(); var b=m.Bytes(address,1); b[0]^=1; m.Put(address,b);
        var r=Round(m,g); Assert.Null(r.Round); Assert.False(r.SuccessfulComparison); Assert.Equal(Memory.Unit,g.UnitPointer);
    }
    [Fact] public void ClosingJournalCatchesTornScalarAndRetainsOriginalOpeningName()
    {
        var m=Setup(); var g=m.Read(); int n=0;
        m.BeforeRead=(a,_)=>{if(a==Pb+56 && ++n==2) m.Put32(Pb+56,2);};
        Assert.Null(Round(m,g).Round); Assert.Equal(2,n);
    }
    [Fact] public void ContextChangesAndCancelledReadsAreUnavailable()
    {
        var m=Setup(); var g=m.Read(); int n=0;
        var r=Warcraft300ObservedRoundReader.Read(m.Bytes,g,Memory.Module,_=>++n==1?Context(m):Context(m) with {World=Memory.World+8});
        Assert.Null(r.Round); Assert.Equal(2,n);
        using var c=new CancellationTokenSource(); c.Cancel(); Assert.Equal("Cancelled",Round(m,g,c.Token).Status);
    }
    [Fact] public void UnterminatedInvalidUtf8OverflowAndShortReadsAreUnavailable()
    {
        var m=Setup(); var g=m.Read(); m.Put(Title,Enumerable.Repeat((byte)'x',96).ToArray()); Assert.Null(Round(m,g).Round);
        m.Put(Title,new byte[]{0xFF,0}); Assert.Null(Round(m,g).Round);
        m.Put64(Frame+0x4C8,ulong.MaxValue); m.Put64(Frame+0x4D0,ulong.MaxValue); Assert.Null(Round(m,g).Round);
        m.ShortAt=Pb+24; Assert.Null(Round(m,g).Round);
    }
    [Fact] public void OriginalAgeAndElapsedAndByteAndCallBudgetsFailClosed()
    {
        var m=Setup(); var g=m.Read(); var start=g.RoundInputs!.StartedTimestamp;
        var stale=Warcraft300ObservedRoundReader.Observation.Read(m.Bytes,g,Memory.Module,_=>Context(m),default,()=>start+4*Stopwatch.Frequency);
        Assert.Null(stale.Round); Assert.Equal(0,stale.ReadCalls);
        int tick=0; var slow=Warcraft300ObservedRoundReader.Observation.Read(m.Bytes,g,Memory.Module,_=>Context(m),default,()=>start+(++tick==1?0:Stopwatch.Frequency));
        Assert.Null(slow.Round); Assert.Equal(0,slow.ReadCalls);
        var bytes=Warcraft300ObservedRoundReader.Read(m.Bytes,g,Memory.Module,r=>{r(Memory.World,8192);r(Memory.World,8192);return Context(m);});
        Assert.Null(bytes.Round); Assert.Equal(16*1024,bytes.RequestedReadBytes); Assert.Equal(2,bytes.ReadCalls);
        var calls=Warcraft300ObservedRoundReader.Read(m.Bytes,g,Memory.Module,r=>{for(int i=0;i<513;i++)r(Memory.World,1);return Context(m);});
        Assert.Null(calls.Round); Assert.Equal(512,calls.ReadCalls);
    }
    [Fact] public void OptionalMissingOrWrongTypedTargetsDoNotInvalidateUnitListing()
    {
        var noTargets=new Memory(); var g=noTargets.Read(); Assert.Null(g.RoundInputs); Assert.Equal(Memory.Unit,g.UnitPointer); Assert.Null(Round(noTargets,g).Round);
        var wrong=Setup(); wrong.Expected.Remove("qg"); wrong.Put32(Qg+48,0);
        var stillUnits=wrong.Read(); Assert.Null(stillUnits.RoundInputs); Assert.Equal(Memory.Unit,stillUnits.UnitPointer);
    }
    [Theory] [InlineData(Eb+56)] [InlineData(Qg+56)] [InlineData(Title)] [InlineData(DialogRecord+0x24)] [InlineData(Frame+0x40)]
    public void ClosingReplayRejectsTornPayloadTitleSerialOrOwner(ulong address)
    {
        var m=Setup(); var g=m.Read(); var replacement=m.Bytes(address,1); replacement[0]^=1; int count=0;
        m.BeforeRead=(a,_)=>{if(a==address && ++count==2)m.Put(address,replacement);};
        Assert.Null(Round(m,g).Round); Assert.Equal(2,count);
    }
    [Fact] public void SelectedInputsAreReadonlyAndUnavailableReadDoesNotResetGrowth()
    {
        var m=Setup(); var g=m.Read(); var inputs=g.RoundInputs!;
        Assert.Throws<NotSupportedException>(()=>((IList<Warcraft300SnapshotNode>)inputs.Selected)[0]=inputs.Selected[1]);
        m.ShortAt=Pb+24; Assert.Null(Round(m,g).Round); m.ShortAt=null;
        Assert.Same(inputs,g.RoundInputs); Assert.Equal(1,Round(m,g).Round); Assert.Equal(Memory.Unit,g.UnitPointer);
    }


    // Synthetic memory only: schema bytes are real, payload values are NOT a live capture.
    [Theory]
    [InlineData("2.320")]
    [InlineData("2.321")]
    [InlineData("2.322")]
    public void BundledVersionFlowsThroughGrowthToObservedRound(string version)
    {
        var source=Map2320GrowthSource.LoadBundled(version);
        var m=Setup(12);
        const ulong arena=0x2000000;
        m.Allocate(arena,source.Globals.Count*1024);
        var rows=source.Globals.Select((p,i)=>(Name:p.Key,Tag:p.Value,
            Address:p.Key==source.GrowthName?Memory.Qr:p.Key==source.PreviousName?Pb:
                p.Key==source.NextName?Eb:p.Key==source.TimerName?Qg:arena+(ulong)i*1024,
            Text:arena+(ulong)i*1024+128)).ToArray();
        m.Put64(Memory.Table+24,rows[0].Address);
        m.Put64(Memory.Table+16,rows[^1].Address+24);
        for(int i=0;i<rows.Length;i++)
        {
            var r=rows[i];
            m.Put64(r.Address+24,i==0?Memory.Table+16:rows[i-1].Address+24);
            m.Put64(r.Address+32,i==rows.Length-1?Memory.Table+17:rows[i+1].Address);
            m.Put64(r.Address+40,r.Text); m.Name(r.Text,r.Name);
            m.Put32(r.Address+48,(uint)r.Tag); m.Put32(r.Address+52,(uint)r.Tag);
        }
        var g=m.Reader.Read(m.Bytes,m.Regions,Memory.Module,m.View,Memory.World,m.Session,source.Globals,source:source);
        var result=Round(m,g);
        Assert.Equal(Memory.Unit,g.UnitPointer);
        Assert.Equal(version,g.RoundInputs!.MapVersion);
        Assert.Equal(new[]{source.PreviousName,source.NextName,source.TimerName}.OrderBy(n=>n),g.RoundInputs.Selected.Select(n=>n.Name).OrderBy(n=>n));
        Assert.Equal(12,result.Round); Assert.Equal(13,result.Eb);
        Assert.StartsWith(version+" ",result.Source);
        Assert.True(result.SuccessfulComparison);
        Assert.False(result.LayoutVerified); Assert.False(result.GameplayReady); Assert.False(result.CanCoach);
        TitleText(m,"|cffFF0000현재 라운드|r : 13|r");
        Assert.Null(Round(m,g).Round);
        var other=Map2320GrowthSource.LoadBundled(version=="2.321"?"2.320":"2.321");
        Assert.Throws<InvalidDataException>(()=>m.Reader.Read(m.Bytes,m.Regions,Memory.Module,m.View,Memory.World,m.Session,source.Globals,source:other));
    }

    [Theory]
    [InlineData("2.320")]
    [InlineData("2.321")]
    [InlineData("2.322")]
    public void BundledSchemaRejectsWrongVersionAndTampering(string version)
    {
        var filename=version=="2.322"?"map-growth-globals-2322.json":version=="2.321"?"map-growth-globals-2321.json":Map2320GrowthSource.FileName;
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Data",filename));
        Assert.Equal(version,Map2320GrowthSource.Load(bytes,version).MapVersion);
        Assert.Throws<InvalidDataException>(()=>Map2320GrowthSource.Load(bytes,version=="2.321"?"2.320":"2.321"));
        bytes[bytes.Length/2]^=1;
        Assert.Throws<InvalidDataException>(()=>Map2320GrowthSource.Load(bytes,version));
    }

    private sealed class Memory
    {
        internal const ulong Module = 0x140000000, Root = 0x200000, Player = 0x300000,
            World = 0x310000, Ui = 0x320000, Instance = 0x400000, Script = 0x410000,
            Manager = 0x420000, Registry = 0x430000, ScanBase = 0x600000, Aggregate = ScanBase + 0xFFF8,
            Table = 0x700000, Qr = 0x710000, Extra = 0x720000, QrName = 0x730000, ExtraName = 0x740000,
            Array = 0x750000, ArrayData = 0x760000, JassTable = 0x800000, Unit = 0x900000,
            NativeTable = 0xA00000, Record = 0xA10000;
        private readonly List<(ulong Address, byte[] Bytes)> blocks = new();
        internal readonly Warcraft300GrowthReader Reader = new();
        internal readonly Dictionary<string, int> Expected = new(StringComparer.Ordinal) { ["QR"] = 12 };
        internal readonly List<(ulong Address, int Length)> Calls = new();
        internal Warcraft300Diagnostic.View View = new(Root, 0, Player);
        internal string Session = "session";
        internal int Enumerations;
        internal Action<ulong, int>? BeforeRead;
        internal ulong? ShortAt;
        internal Func<IEnumerable<MemoryRegion>>? RegionOverride = null;
        internal Memory()
        {
            foreach (var a in new[] { Root, Player, World, Ui, Instance, Script, Manager, Registry,
                Table, Qr, Extra, QrName, ExtraName, Array, ArrayData, JassTable, Unit, NativeTable, Record }) Allocate(a, 0x3000);
            Allocate(ScanBase, 0x10030);
            foreach (var rva in new ulong[] { 0x2E9AD00, 0x2F5EF00, 0x2F85360, 0x2F807F0 }) Allocate(Module + rva, 8);
            ulong encoded;
            unchecked { encoded = BitOperations.RotateRight(((Root - 0x2D2C27903E7F5D3DUL) ^ 0x3A11C7B7EF67132BUL) - 0x5BE06F37FC9B5B29UL, 29); }
            Put64(Module + 0x2E9AD00, encoded);
            Put64(Root, Module + 0x26C8C70); Put32(Root + 0x2698, 28); SetView(0);
            Put64(Player, Module + 0x26C87D8); Put64(World, Module + 0x2764A20); Put64(World + 0x40, Ui);
            Put64(Ui, Module + 0x275ED08); Put64(Module + 0x2F5EF00, Ui); Put64(Module + 0x2F85360, Ui);
            Put64(Root + 0x25D0, Instance); Put64(Root + 0x25E0, Script); Put64(Root + 0x2620, Manager);
            Put64(Instance, Module + 0x27ECBC0); Put64(Script, Module + 0x27ECC40);
            Put64(Module + 0x2F807F0, Registry);
            Put64(Aggregate, Instance); Put64(Aggregate + 8, Module + 0x13F3070); Put64(Aggregate + 16, Table);
            Put64(Table, Module + 0x2809B78); Put32(Table + 8, 24); Put64(Table + 24, Qr); Put64(Table + 16, Extra + 24);
            Put64(Qr + 24, Table + 16); Put64(Qr + 32, Extra); Put64(Qr + 40, QrName);
            Put32(Qr + 48, 12); Put32(Qr + 52, 12); Put64(Qr + 56, Array); Name(QrName, "QR");
            Put64(Extra + 24, Qr + 24); Put64(Extra + 32, Table + 17); Put64(Extra + 40, ExtraName);
            Put32(Extra + 48, 0); Put32(Extra + 52, 7); Name(ExtraName, "extra");
            Put64(Array, Module + 0x2809EF8); Put32(Array + 8, 1); Put64(Array + 16, ArrayData); Put32(Array + 24, 4);
            Put32(ArrayData, 0x100000); Put32(Manager + 0x290, 1); Put64(Manager + 0x298, JassTable);
            Put32(JassTable, 1); Put64(JassTable + 8, Unit);
            Put64(Unit, Module + 0x2792E78); Put32(Unit + 0x18, 0); Put32(Unit + 0x1C, 5);
            Put32(Unit + 0x1C0, 27); Put32(Unit + 0x178, 0x48303031);
            Put32(Registry + 0x30, 1); Put64(Registry + 0x18, NativeTable);
            Put32(NativeTable, 0xFFFFFFFE); Put64(NativeTable + 8, Record);
            Put32(Record + 0x24, 5); Put32(Record + 0x18, 0x2B61676C); Put64(Record + 0x90, Unit);
        }
        internal void SetView(ushort slot)
        {
            Put(Root + 0x262C, BitConverter.GetBytes(slot)); Put64(Root + 0x26A0 + slot * 8UL, Player);
            View = new(Root, slot, Player);
        }
        internal void Allocate(ulong a, int length) => blocks.Add((a, new byte[length]));
        internal void Put(ulong a, byte[] value)
        {
            var block = blocks.Single(b => a >= b.Address && a - b.Address + (ulong)value.Length <= (ulong)b.Bytes.Length);
            value.CopyTo(block.Bytes, (int)(a - block.Address));
        }
        internal void Put64(ulong a, ulong value) => Put(a, BitConverter.GetBytes(value));
        internal void Put32(ulong a, uint value) => Put(a, BitConverter.GetBytes(value));
        internal void Name(ulong a, string value) { Put(a, new byte[512]); Put(a, Encoding.ASCII.GetBytes(value + "\0")); }
        internal byte[] Bytes(ulong a, int n)
        {
            BeforeRead?.Invoke(a, n); Calls.Add((a, n));
            var found = blocks.Where(b => a >= b.Address && a - b.Address + (ulong)n <= (ulong)b.Bytes.Length).ToArray();
            if (found.Length != 1) throw new InvalidDataException("Unmapped synthetic read");
            var bytes = found[0].Bytes.AsSpan((int)(a - found[0].Address), n).ToArray();
            return ShortAt == a ? bytes[..^1] : bytes;
        }
        internal IEnumerable<MemoryRegion> Regions()
        {
            Enumerations++;
            return RegionOverride?.Invoke() ?? new[] { new MemoryRegion(ScanBase, 0x10030) };
        }
        internal Warcraft300GrowthObservation Read(CancellationToken token = default,
            int discoveryBlockBytes = Warcraft300GrowthReader.DefaultDiscoveryBlockBytes) =>
            Reader.Read(Bytes, Regions, Module, View, World, Session, Expected, token, discoveryBlockBytes);
    }
}
