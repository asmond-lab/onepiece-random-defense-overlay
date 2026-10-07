using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OrandOverlay;

// Same Growth call's selected identifying inputs only. No scalar/handle/title payload and no Prelude dependency.
internal sealed class Warcraft300RoundInputs
{
    private readonly byte[] ownerHeader, tableHeader, instanceHeader, scriptHeader;
    internal string MapVersion { get; private init; } = "2.320";
    internal string PreviousName => MapVersion switch { "2.323" => "Av", "2.322" => "GR", "2.321" => "Ag", _ => "pb" };
    internal string NextName => MapVersion switch { "2.323" => "Ov", "2.322" => "hR", "2.321" => "lg", _ => "Eb" };
    internal string TimerName => MapVersion switch { "2.323" => "hp", "2.322" => "Vs", "2.321" => "yp", _ => "qg" };
    internal Guid Invocation { get; } = Guid.NewGuid();
    internal long StartedTimestamp { get; }
    internal string Session { get; }
    internal ulong Module { get; }
    internal Warcraft300Diagnostic.View View { get; }
    internal ulong World { get; }
    internal ulong Ui { get; }
    internal ulong Instance { get; }
    internal ulong Script { get; }
    internal ulong Manager { get; }
    internal ulong Registry { get; }
    internal ulong Owner { get; }
    internal ulong Table { get; }
    internal IReadOnlyList<Warcraft300SnapshotNode> Selected { get; }
    private Warcraft300RoundInputs(long started, string session, ulong module, Warcraft300Diagnostic.View view, ulong world, ulong ui,
        ulong instance, ulong script, ulong manager, ulong registry, ulong owner, ulong table, byte[] ownerHeader, byte[] tableHeader,
        byte[] instanceHeader, byte[] scriptHeader, Warcraft300SnapshotNode[] selected)
    {
        StartedTimestamp=started; Session=session; Module=module; View=view; World=world; Ui=ui; Instance=instance; Script=script;
        Manager=manager; Registry=registry; Owner=owner; Table=table;
        this.ownerHeader=ownerHeader.ToArray(); this.tableHeader=tableHeader.ToArray();
        this.instanceHeader=instanceHeader.ToArray(); this.scriptHeader=scriptHeader.ToArray();
        Selected=Array.AsReadOnly(selected.ToArray());
    }
    internal static Warcraft300RoundInputs? TryCreate(long started, string session, ulong module, Warcraft300Diagnostic.View view,
        ulong world, ulong ui, ulong instance, ulong script, ulong manager, ulong registry, ulong owner, ulong table,
        byte[] ownerHeader, byte[] tableHeader, byte[] instanceHeader, byte[] scriptHeader,
        IEnumerable<(string Name, ulong Address, byte[] Metadata)> selected, Map2320GrowthSource? source = null)
    {
        try
        {
            var rows=selected.Take(4).ToArray();
            if(rows.Length!=3 || rows.Select(n=>n.Name).Distinct(StringComparer.Ordinal).Count()!=3 ||
                ownerHeader.Length!=24 || tableHeader.Length!=72 || instanceHeader.Length!=48 || scriptHeader.Length!=16) return null;
            var nodes=rows.Select(n=>new Warcraft300SnapshotNode(n.Name,n.Address,n.Metadata)).ToArray();
            foreach(var n in nodes)
            {
                uint tag=n.Name == (source?.PreviousName ?? "pb") || n.Name == (source?.NextName ?? "Eb") ? 4U : n.Name == (source?.TimerName ?? "qg") ? 7U : 0U;
                if(tag==0 || n.RuntimeTag!=tag || n.DeclaredTag!=tag) return null;
            }
            var ordered=nodes.OrderBy(n=>n.Address).ToArray();
            if(ordered[0].Address+64>ordered[1].Address || ordered[1].Address+64>ordered[2].Address) return null;
            return new(started,session,module,view,world,ui,instance,script,manager,registry,owner,table,
                ownerHeader,tableHeader,instanceHeader,scriptHeader,nodes) { MapVersion = source?.MapVersion ?? "2.320" };
        }
        catch(Exception e) when(e is InvalidDataException or ArgumentException or OverflowException) { return null; }
    }
    internal void RecheckHeaders(Func<ulong,int,byte[]> read)
    {
        void Same(ulong a,byte[] b) { if(!b.AsSpan().SequenceEqual(read(a,b.Length))) throw new InvalidDataException("Round context changed"); }
        Same(Owner,ownerHeader); Same(Table,tableHeader); Same(Instance,instanceHeader); Same(Script,scriptHeader);
    }
}

// Observed reference only, not RecognitionResult.Round, layout validation, coaching, or gameplay readiness.
// Caller still owns executable/map binding and the post-discovery sample's 3-second freshness deadline.
internal static class Warcraft300ObservedRoundReader
{
    internal const int MaximumReadBytes=16*1024, MaximumReadCalls=512, MaximumMilliseconds=250;
    internal readonly record struct Context(string SessionKey, ulong Module, Warcraft300Diagnostic.View View, ulong World);
    internal static Observation Read(Func<ulong,int,byte[]> read, Warcraft300GrowthObservation growth, ulong module,
        Func<Func<ulong,int,byte[]>,Context> currentContext, CancellationToken token=default) =>
        Observation.Read(read,growth,module,currentContext,token,Stopwatch.GetTimestamp);

    internal sealed class Observation
    {
        public int? Round { get; }
        public int? Pb { get; }
        public int? Eb { get; }
        public string Source { get; }
        public bool SuccessfulComparison { get; }
        public string Status { get; }
        public double ElapsedMilliseconds { get; }
        public int RequestedReadBytes { get; }
        public int ReadCalls { get; }
        public bool CanCoach => false;
        public bool LayoutVerified => false;
        public bool GameplayReady => false;
        private Observation(int? round,int? pb,int? eb,bool compared,string status,double elapsed,int bytes,int calls, string source = "2.320 pb/Eb+ownedTimerDialogTitle")
        { Source=source; Round=round; Pb=pb; Eb=eb; SuccessfulComparison=compared; Status=status; ElapsedMilliseconds=elapsed; RequestedReadBytes=bytes; ReadCalls=calls; }
        // Factory accepts only a fresh validated Growth input and delegates, never UI-supplied round/scalar numbers.
        internal static Observation Read(Func<ulong,int,byte[]> read, Warcraft300GrowthObservation growth, ulong module,
            Func<Func<ulong,int,byte[]>,Context> currentContext, CancellationToken token, Func<long> timestamp)
        {
            var started=timestamp(); long checkedAt=started; int bytes=0,calls=0; var journal=new List<(ulong Address,byte[] Bytes)>(); bool recording=true;
            string failure="Unavailable";
            double Elapsed()=>Stopwatch.GetElapsedTime(started,timestamp()).TotalMilliseconds;
            var sourceLabel = growth.RoundInputs is { } ri ? $"{ri.MapVersion} {ri.PreviousName}/{ri.NextName}+ownedTimerDialogTitle" : "Unavailable";
            Observation Unknown()=>new(null,null,null,false,failure,Elapsed(),bytes,calls,sourceLabel);
            try
            {
                var input=growth.RoundInputs;
                void Need(bool ok) { if(!ok) throw new InvalidDataException("Observed round unavailable"); }
                void Check()
                {
                    token.ThrowIfCancellationRequested(); var now=timestamp(); checkedAt=now;
                    Need(now>=started && Stopwatch.GetElapsedTime(started,now).TotalMilliseconds<MaximumMilliseconds);
                    Need(input is not null && now>=input.StartedTimestamp && Stopwatch.GetElapsedTime(input.StartedTimestamp,now)<TimeSpan.FromSeconds(3)); // sample window, not discovery walk
                }
                ulong Add(ulong a,ulong b) => checked(a+b);
                byte[] R(ulong a,int n)
                {
                    Check(); Need(n>0 && a>=0x10000 && a<=0x7FFFFFFFFFFF && (ulong)(n-1)<=0x7FFFFFFFFFFF-a);
                    Need(n<=MaximumReadBytes-bytes && calls<MaximumReadCalls); bytes+=n; calls++;
                    var result=read(a,n); Check(); Need(result is not null && result.Length==n);
                    // Copy immediately: caller-owned buffers must never mutate our opening journal.
                    var copy=result!.ToArray(); if(recording) journal.Add((a,copy)); return copy.ToArray();
                }
                ulong Q(ulong a)=>BitConverter.ToUInt64(R(a,8)); uint D(ulong a)=>BitConverter.ToUInt32(R(a,4));
                void Typed(ulong a,ulong rva)=>Need(Q(a)==Add(module,rva));
                string Text(ulong a,int limit)
                {
                    var b=R(a,limit); var end=Array.IndexOf(b,(byte)0); Need(end>=0);
                    return new UTF8Encoding(false,true).GetString(b,0,end);
                }
                Check(); Need(input is not null && module==input.Module && growth.SessionKey==input.Session && growth.CurrentView==input.View &&
                    growth.World==input.World && growth.Instance==input.Instance && growth.Script==input.Script &&
                    growth.MapManager==input.Manager && growth.NativeRegistry==input.Registry && growth.OwnerAggregate==input.Owner && growth.DataTable==input.Table);
                var s=input!; var expected=new Context(s.Session,module,s.View,s.World);
                Need(currentContext(R)==expected); Check();
                Need(Warcraft300Diagnostic.ReadView(R,module)==s.View && s.View.Slot<=3);
                Need(Q(Add(module,0x2F5EF00))==s.Ui && Q(Add(module,0x2F85360))==s.Ui);
                Typed(s.Ui,0x275ED08); Typed(s.World,0x2764A20); Need(Q(Add(s.World,0x40))==s.Ui);
                Need(Q(Add(s.View.Root,0x25D0))==s.Instance && Q(Add(s.View.Root,0x25E0))==s.Script &&
                    Q(Add(s.View.Root,0x2620))==s.Manager && Q(Add(module,0x2F807F0))==s.Registry);
                s.RecheckHeaders(R);
                foreach(var node in s.Selected)
                { uint tag=node.Name==s.TimerName?7U:4U; Need(node.RuntimeTag==tag && node.DeclaredTag==tag); node.Recheck(R); }
                var pb=BitConverter.ToInt32(R(Add(s.Selected.Single(n=>n.Name==s.PreviousName).Address,56),4));
                var eb=BitConverter.ToInt32(R(Add(s.Selected.Single(n=>n.Name==s.NextName).Address,56),4));
                var handle=D(Add(s.Selected.Single(n=>n.Name==s.TimerName).Address,56));
                Need(handle>0x100000); var index=handle-0x100000; var limit=D(Add(s.Manager,0x290)); var table=Q(Add(s.Manager,0x298));
                Need(limit>0 && limit<=1048576 && index<limit);
                var entry=R(Add(table,24UL*index),24); Need(BitConverter.ToUInt32(entry)>0);
                var dialog=BitConverter.ToUInt64(entry,8); Typed(dialog,0x2730B08);
                void Resolve(ulong h,ulong obj,ulong vtable)
                {
                    uint i=(uint)h&0x7FFFFFFF; bool alternate=((uint)h&0x80000000)!=0;
                    var count=D(Add(s.Registry,alternate?0x68UL:0x30UL)); var slots=Q(Add(s.Registry,alternate?0x50UL:0x18UL));
                    Need(count>0 && count<=262144 && i<count); var slot=R(Add(slots,16UL*i),16);
                    Need(BitConverter.ToUInt32(slot)==0xFFFFFFFE); var record=BitConverter.ToUInt64(slot,8);
                    Need(D(Add(record,0x18))==0x2B61676C && D(Add(record,0x24))==(uint)(h>>32) && Q(Add(record,0x30))==0 &&
                        Q(Add(record,0x90))==obj && Q(Add(obj,0x18))==h); Typed(obj,vtable);
                }
                Resolve(Q(Add(dialog,0x18)),dialog,0x2730B08);
                var dialogUi=Q(Add(dialog,0x58)); Typed(dialogUi,0x2768190);
                Need(Q(Add(dialogUi,0x40))==s.Ui && Text(Q(Add(dialogUi,0x280)),32)=="TimerDialog");
                Resolve(Q(Add(dialog,0x60)),Q(Add(dialogUi,0x2D8)),0x26E04E0);
                var titleFrame=Q(Add(dialogUi,0x298)); Typed(titleFrame,0x228F5D0);
                Need(Q(Add(titleFrame,0x40))==dialogUi && Text(Q(Add(titleFrame,0x280)),32)=="TimerDialogTitle");
                var titleAddress=Q(Add(titleFrame,0x4C8)); Need(Q(Add(titleFrame,0x4D0))==titleAddress);
                var title=Text(titleAddress,96); var titleRound=WarcraftCurrentRoundReader.ParseTitle(title);
                recording=false;
                foreach(var item in journal) Need(item.Bytes.AsSpan().SequenceEqual(R(item.Address,item.Bytes.Length)));
                Need(currentContext(R)==expected); Check();
                bool pair=pb>=0 && pb<=65 && eb==pb+1;
                bool positive=pair && pb>0 && titleRound==pb;
                bool pre=pair && pb==0 && title=="|cffFF0000 1라운드 시작까지|r";
                Check();
                return new(positive?pb:null,pb,eb,positive,positive?"ObservedReferenceRound":pre?"PreRound":"Unavailable",
                    Stopwatch.GetElapsedTime(started,checkedAt).TotalMilliseconds,bytes,calls,sourceLabel);
            }
            catch(OperationCanceledException) { failure="Cancelled"; return Unknown(); }
            catch(Exception) { return Unknown(); } // Round evidence must NEVER invalidate an otherwise successful unit listing.
        }
    }
}
