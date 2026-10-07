using System.Text.Json;
using Xunit;

namespace OrandOverlay.Tests;

public sealed partial class Warcraft300DiagnosticTests
{
    private static MemoryProfile Profile(string change = "")
    {
        var json = """
        {"ProfileSchemaVersion":2,"Layout":"Warcraft30024268Diagnostic","OwnerFieldBytes":4,
        "ProfileId":"diagnostic-fixture","FileVersion":"3.0.0.24268","sha256":"BD2A0DC256289DE45287BB60F3725B88F1235216D5177984377FE1CA22840A12",
        "LocatorKind":2,"CountOffset":3080,"EntriesPointerOffset":3088,"OwnerOffset":448,
        "RawcodeOffset":376,"MinimumUnitObjects":1,"Enabled":true,"Verified":true}
        """;
        if (change.Length > 0) { var pair = change.Split('='); json = json.Replace(pair[0], pair[1]); }
        return JsonSerializer.Deserialize<MemoryProfile>(json)!;
    }
    [Fact] public void ProductionAlwaysRejectsAndExplicitSessionIsRequired()
    {
        var p = Profile();
        Assert.Empty(MemoryProfileValidator.Validate(p));
        Assert.False(MemoryProfileValidator.CanActivate(p, out _));
        Assert.False(Warcraft300Diagnostic.SessionAllows(p, false));
        Assert.True(Warcraft300Diagnostic.SessionAllows(p, true));
    }
    [Fact] public void ExplicitSessionDoesNotOverrideDisabledProfile()
    {
        var p = Profile("\"Enabled\":true=\"Enabled\":false");
        Assert.False(Warcraft300Diagnostic.SessionAllows(p, true));
    }
    [Theory]
    [InlineData("\"ProfileSchemaVersion\":2=\"ProfileSchemaVersion\":1")]
    [InlineData("Warcraft30024268Diagnostic=unknown")]
    [InlineData("3.0.0.24268=3.0.0.24267")]
    [InlineData("BD2A0D=AD2A0D")]
    [InlineData("\"OwnerFieldBytes\":4=\"OwnerFieldBytes\":1")]
    [InlineData("\"CountOffset\":3080=\"CountOffset\":3072")]
    public void BadSchemaLayoutHashAndLegacyOffsetsRejected(string change)
    {
        var p = Profile(change);
        Assert.NotEmpty(MemoryProfileValidator.Validate(p));
        Assert.False(Warcraft300Diagnostic.SessionAllows(p, true));
    }
    [Theory] [InlineData(0UL)] [InlineData(ulong.MaxValue)] [InlineData(0xFEDCBA9876543210UL)]
    public void DecodeUsesWrappingArithmetic(ulong encoded)
    {
        var mask = (System.Numerics.BigInteger.One << 64) - 1;
        var x = new System.Numerics.BigInteger(encoded);
        x = ((x << 29) | (x >> 35)) & mask;
        x = (x + 0x5BE06F37FC9B5B29UL) & mask;
        x = ((x ^ 0x3A11C7B7EF67132BUL) + 0x2D2C27903E7F5D3DUL) & mask;
        Assert.Equal((ulong)x, Warcraft300Diagnostic.DecodeRoot(encoded));
    }
    [Theory] [InlineData(28U)] [InlineData(256U)] [InlineData(0x1000000U)] [InlineData(uint.MaxValue)]
    public void FullDwordOwnerIsRangeChecked(uint owner) => Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.Owner(owner));
    [Fact] public void LegacyProfileStillActivates()
    {
        var p = new MemoryProfile { ProfileId="legacy", FileVersion="2.0.4", Sha256=new string('A',64),
            LocatorKind=MemoryLocatorKind.ModuleOffset, Enabled=true, Verified=true };
        Assert.True(MemoryProfileValidator.CanActivate(p, out _));
        Assert.True(Warcraft300Diagnostic.SessionAllows(p, false));
    }
    private sealed class Fixture
    {
        public const ulong B=0x140000000, Game=0x200000000, Player=0x210000000, Frame=0x220000000, Array=0x230000000, Unit=0x240000000, Ui=0x250000000;
        private readonly Dictionary<ulong, byte> bytes = new();
        public void Put(ulong a, byte[] data) { for(var i=0;i<data.Length;i++) bytes[a+(ulong)i]=data[i]; }
        public void Q(ulong a, ulong v) => Put(a,BitConverter.GetBytes(v));
        public void D(ulong a, uint v) => Put(a,BitConverter.GetBytes(v));
        public byte[] Read(ulong a,int n) => Enumerable.Range(0,n).Select(i=>bytes.GetValueOrDefault(a+(ulong)i)).ToArray();
        public Fixture()
        {
            var rotated=unchecked(((Game-0x2D2C27903E7F5D3DUL)^0x3A11C7B7EF67132BUL)-0x5BE06F37FC9B5B29UL);
            Q(B+0x2E9AD00,(rotated>>29)|(rotated<<35));
            Q(Game,B+0x26C8C70); D(Game+0x2698,28); Put(Game+0x262C,BitConverter.GetBytes((ushort)6));
            Q(Game+0x26A0+6*8,Player); Q(Player,B+0x26C87D8);
            Q(B+0x2F5EF00,Ui); Q(B+0x2F85360,Ui); Q(Ui,B+0x275ED08); Q(Frame+0x40,Ui);
            Q(Frame,B+0x2764A20); D(Frame+0xC08,1); Q(Frame+0xC10,Array); Q(Array,Unit);
            Q(Unit,B+0x2792E78); D(Unit+0x1C0,6); D(Unit+0x178,0x68303031);
            Q(B+0x2F807F0,0x260000000); Q(0x260000018,0x270000000); D(0x260000030,1);
            D(Unit+0x18,0); D(Unit+0x1C,5); D(0x270000000,0xFFFFFFFE); Q(0x270000008,0x280000000);
            D(0x280000018,0x2B61676C); D(0x280000024,5); Q(0x280000090,Unit);
        }
        public Warcraft300Diagnostic.Inventory Run() => Warcraft300Diagnostic.ReadInventory(Read,B,Frame,Profile());
    }
    [Fact] public void TypedFixtureSelectsCurrentViewInventory() { var s=new Fixture().Run(); Assert.Equal(6,s.CurrentView.Slot); Assert.Equal(1,s.Owned); }
    [Theory] [InlineData("game")] [InlineData("player")] [InlineData("frame")] [InlineData("unit")]
    [InlineData("slot")] [InlineData("table")] [InlineData("owner")]
    [InlineData("ui")] [InlineData("uiAlias")] [InlineData("frameParent")]
    public void InvalidStructureRejected(string kind)
    {
        var f=new Fixture();
        switch(kind)
        {
            case "game": f.Q(Fixture.Game,0); break;
            case "player": f.Q(Fixture.Player,0); break;
            case "frame": f.Q(Fixture.Frame,0); break;
            case "unit": f.Q(Fixture.Unit,0); break;
            case "ui": f.Q(Fixture.Ui,0); break;
            case "uiAlias": f.Q(Fixture.B+0x2F85360,Fixture.Ui+8); break;
            case "frameParent": f.Q(Fixture.Frame+0x40,Fixture.Ui+8); break;
            case "slot": f.Put(Fixture.Game+0x262C,BitConverter.GetBytes((ushort)24)); break;
            case "table": f.D(Fixture.Game+0x2698,27); break;
            case "owner": f.D(Fixture.Unit+0x1C0,262); break;
        }
        Assert.Throws<InvalidDataException>(()=>f.Run());
    }
    [Theory] [InlineData("root")] [InlineData("slot")] [InlineData("player")]
    public void UnstableViewRejected(string field)
    {
        var f=new Fixture(); var reads=0;
        var address = field == "root" ? Fixture.B+0x2E9AD00 : field == "slot" ? Fixture.Game+0x262C : Fixture.Game+0x26A0+6*8;
        byte[] Read(ulong a,int n) { var data=f.Read(a,n); if(a==address && ++reads==2) data[0]^=8; return data; }
        Assert.Throws<InvalidDataException>(()=>Warcraft300Diagnostic.ReadView(Read,Fixture.B));
    }
    [Fact] public void RootZeroIsNeverAFallback()
    {
        var f=new Fixture();
        Assert.Throws<InvalidDataException>(()=>Warcraft300Diagnostic.ReadInventory(f.Read,Fixture.B,0,Profile()));
    }
    [Theory] [InlineData(0U)] [InlineData(27U)]
    public void DwordOwnerBoundaryAccepted(uint owner) => Assert.Equal((byte)owner,Warcraft300Diagnostic.Owner(owner));
    [Fact] public void ReallocatedSameAddressAndRawcodeIsRejected()
    {
        var f=new Fixture(); var reads=0;
        byte[] Read(ulong a,int n)
        {
            if(a==Fixture.Unit+0x18 && ++reads==2) { f.D(Fixture.Unit+0x1C,6); f.D(0x280000024,6); }
            return f.Read(a,n);
        }
        Assert.Throws<InvalidDataException>(()=>Warcraft300Diagnostic.ReadInventory(Read,Fixture.B,Fixture.Frame,Profile()));
    }
    [Fact] public void UnallocatedUnitCannotEnterCounts()
    {
        var f=new Fixture(); f.D(0x270000000,0);
        Assert.Throws<InvalidDataException>(()=>f.Run());
    }
    [Fact] public void ChangedVectorRejected()
    {
        var f=new Fixture(); var reads=0;
        byte[] Read(ulong a,int n) { var data=f.Read(a,n); if(a==Fixture.Array && ++reads%2==0) data[0]^=8; return data; }
        Assert.Throws<InvalidDataException>(()=>Warcraft300Diagnostic.ReadInventory(Read,Fixture.B,Fixture.Frame,Profile()));
        Assert.Equal(4,reads);
    }
}
