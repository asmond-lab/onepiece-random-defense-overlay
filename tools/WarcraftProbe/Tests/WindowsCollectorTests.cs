using Xunit;

namespace WarcraftProbe.Tests;

public sealed class WindowsCollectorTests
{
    private const ulong Base = 0x140000000;
    private static WindowsCollector.MemoryRegion Region(ulong a, ulong n, uint p = 4) => new(a,n,0x1000,p,0x1000000);
    [Theory] [InlineData(1U)] [InlineData(0x104U)] [InlineData(0x10U)] [InlineData(0U)]
    public void EntireRangePreflightRejectsBeforeAnyRpm(uint protection)
    {
        int rpm = 0, queries = 0; var budget = new WindowsCollector.Budget(() => TimeSpan.Zero);
        WindowsCollector.MemoryRegion? Query(ulong a) { queries++; return a == Base ? Region(Base,8) : Region(Base+8,8,protection); }
        Assert.Throws<WindowsCollector.UnreadableException>(() => WindowsCollector.ReadExact(Base,16,Query,(_,n)=>{rpm++;return new byte[n];},budget));
        Assert.Equal(0,rpm); Assert.Equal(2,queries); Assert.Equal(0,budget.Bytes);
    }
    [Theory] [InlineData(-1)] [InlineData(1)]
    public void ShortAndOversizedReadsFail(int adjustment)
    {
        var b=new WindowsCollector.Budget(()=>TimeSpan.Zero);
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.ReadExact(Base,8,a=>Region(a,8),(_,n)=>new byte[n+adjustment],b));
        Assert.Equal(8,b.Bytes);
    }
    [Fact] public void CancellationBeforeQueryAndAfterRpmFailsClosed()
    {
        using var c=new CancellationTokenSource(); int reads=0,queries=0;
        var b=new WindowsCollector.Budget(()=>TimeSpan.Zero,c.Token);
        Assert.Throws<OperationCanceledException>(()=>WindowsCollector.ReadExact(Base,8,a=>{queries++;return Region(a,8);},(_,n)=>{reads++;c.Cancel();return new byte[n];},b,c.Token));
        Assert.Equal(1,reads); Assert.Equal(1,queries);
        Assert.Throws<OperationCanceledException>(()=>WindowsCollector.ReadExact(Base,8,a=>{queries++;return Region(a,8);},(_,n)=>new byte[n],b,c.Token));
        Assert.Equal(1,queries);
    }
    [Fact] public void ExactByteAndQueryLimitsAreInclusiveButDeadlineIsExclusive()
    {
        var time=TimeSpan.Zero; var b=new WindowsCollector.Budget(()=>time);
        b.Charge((int)WindowsCollector.MaximumReadBytes); Assert.Equal(WindowsCollector.MaximumReadBytes,b.Bytes);
        Assert.Throws<InvalidDataException>(()=>b.Charge(1));
        for(int i=0;i<WindowsCollector.MaximumQueries;i++) b.Query();
        Assert.Throws<InvalidDataException>(()=>b.Query());
        time=TimeSpan.FromMilliseconds(9999); b.Check(); time=TimeSpan.FromSeconds(10); Assert.Throws<InvalidDataException>(b.Check);
    }
    [Fact] public void DeadlineReachedInsideQueryMeansZeroRpm()
    {
        var time=TimeSpan.Zero; var b=new WindowsCollector.Budget(()=>time); var reads=0;
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.ReadExact(Base,8,a=>{time=TimeSpan.FromSeconds(10);return Region(a,8);},(_,n)=>{reads++;return new byte[n];},b)); Assert.Equal(0,reads);
    }
    [Fact] public void EpochMismatchIsRejectedBeforeCopyIo()
    {
        var start=DateTimeOffset.Parse("2020-01-01T00:00:00Z"); var i=new WindowsCollector.Identity(123,start,Base,4096,@"C:\Game\Warcraft III.exe"); var io=0;
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.IdentityBeforeCopy(()=>WindowsCollector.Pin(123,start.AddTicks(1),()=>new[]{i}),_=>++io,out _)); Assert.Equal(0,io);
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.Pin(123,null,()=>throw new Exception("must not discover")));
        Assert.Equal(i,WindowsCollector.Pin(null,null,()=>new[]{i}));
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.Pin(null,null,()=>new[]{i,i with {Pid=124}}));
    }
    [Theory]
    [InlineData(@"C:\Game\Warcraft III.exe")]
    [InlineData(@"\\server\share\copy.exe")]
    [InlineData(@"\\?\C:\copy.exe")]
    [InlineData(@"C:\copy.exe:stream")]
    [InlineData(@"C:\PROGRA~1\copy.exe")]
    [InlineData(@"C:\x\..\copy.exe")]
    [InlineData(@"C:\NUL\copy.exe")]
    public void InstalledNetworkDeviceAdsAndAliasPathsRejectedWithoutIo(string copy) => Assert.Throws<InvalidDataException>(()=>WindowsCollector.ValidateCopyPath(copy,@"C:\Game\Warcraft III.exe"));
    [Fact] public void ExplicitCanonicalCopyPathAccepted() => Assert.Equal(@"C:\Copies\copy.exe",WindowsCollector.ValidateCopyPath(@"C:\Copies\copy.exe",@"C:\Game\Warcraft III.exe"));
    [Fact] public void CompleteMapClipsAndMergesButMissingTailNeverReturnsPartial()
    {
        var result=WindowsCollector.EnumerateRegions(Base,24,a=>a<Base+16?Region(a,8):Region(a,8,1),()=>{});
        Assert.Equal(2,result.Length); Assert.Equal(0U,result[0].Rva); Assert.Equal(16UL,result[0].Size); Assert.Equal(16U,result[1].Rva);
        Assert.Throws<InvalidDataException>(()=>WindowsCollector.EnumerateRegions(Base,24,a=>a<Base+16?Region(a,8):null,()=>{}));
    }
    [Fact] public void UnknownBuildNeverAttemptsCodeRead()
    {
        var i=Known300AdapterTests.Image() with {Sha256=new string('0',64)};
        Assert.Empty(WindowsCollector.CheckCode(i,Base,(_,_)=>throw new Exception("Unknown code read")));
    }
    [Fact] public void NineFiniteFunctionsOnlyAndNoAccessIsExplicitlyBlocked()
    {
        int rpm=0; var b=new WindowsCollector.Budget(()=>TimeSpan.Zero); var requested=new List<int>();
        var result=WindowsCollector.CheckCode(Known300AdapterTests.Image(),Base,(a,n)=>
        { requested.Add(n); return WindowsCollector.ReadExact(a,n,x=>Region(x,(ulong)n,1),(_,s)=>{rpm++;return new byte[s];},b); });
        Assert.Equal(9,result.Length); Assert.Equal(0,rpm); Assert.Equal(WindowsCollector.KnownFunctions.Select(f=>(int)f.Size),requested);
        Assert.All(result,c=>{Assert.Equal("CodeCheckBlocked",c.Status);Assert.Null(c.Sha256);});
    }
}
