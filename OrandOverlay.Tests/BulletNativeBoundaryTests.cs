using Xunit;
namespace OrandOverlay.Tests;

public sealed class BulletNativeBoundaryTests
{
    private static BulletNativeGlobalsTests.GlobalsMemory Navigation()
    {
        var m = new BulletNativeGlobalsTests.GlobalsMemory();
        m.Array("JP", 9, 0, 2); m.Array("SP", 9, 0, 0); m.Array("cE", 13, 0, 1); m.Array("Ly", 13, 0, 1); return m;
    }
    [Theory]
    [InlineData("owner")][InlineData("short")][InlineData("type")][InlineData("map")][InlineData("version")]
    [InlineData("missing")][InlineData("changed")][InlineData("header")][InlineData("node")]
    public void UnverifiedNavigationNeverBecomesZeroOrNativeSelection(string fault)
    {
        var m = Navigation(); byte? slot = fault == "owner" ? null : (byte)1;
        if (fault == "short") m.Array("JP", 9, 2);
        if (fault == "type") m.Put(m.Nodes["JP"] + 52, 13);
        if (fault == "missing") m.Put(m.Nodes["JP"] + 56, 0UL);
        var changed = false;
        byte[] Read(ulong address, int size)
        {
            var bytes = m.Read(address, size);
            if (!changed && address == m.Data("SP") && fault is "changed" or "header" or "node")
            {
                changed = true;
                if (fault == "changed") m.Put(m.Data("JP") + 4, 3);
                if (fault == "header") m.Array("JP", 9, 0, 2);
                if (fault == "node") m.Put(m.Nodes["JP"] + 40, 0UL);
            }
            return bytes;
        }
        var result = new WarcraftNavigationReader().Read(Read, fault == "version" ? "wrong" : "2.0.4.23745",
            fault == "map" ? "wrong" : RouteQuestCatalog.MapScriptSha256, slot, m.Nodes["JP"]);
        Assert.Equal(NativeNavigationStatus.Unknown, result.Status);
        Assert.Null(result.OptionId);
        Assert.Equal("manual", result.Resolve("manual"));
    }
    [Theory]
    [InlineData(0, 0, 0, 0, NativeNavigationStatus.Unselected)]
    [InlineData(0, 0, 1, 1, NativeNavigationStatus.Unknown)]
    [InlineData(2, 0, 0, 1, NativeNavigationStatus.Conflict)]
    [InlineData(0, 2, 1, 0, NativeNavigationStatus.Conflict)]
    [InlineData(4, 0, 1, 1, NativeNavigationStatus.Conflict)]
    [InlineData(-1, 0, 1, 1, NativeNavigationStatus.Conflict)]
    [InlineData(1, 0, 1, 1, NativeNavigationStatus.Selected)]
    [InlineData(3, 0, 1, 1, NativeNavigationStatus.Selected)]
    public void FamilyAndFlagsAreCrossChecked(int jp, int sp, int ce, int ly, NativeNavigationStatus expected)
    {
        var m = Navigation(); m.Array("JP", 9, jp); m.Array("SP", 9, sp); m.Array("cE", 13, ce); m.Array("Ly", 13, ly);
        var result = new WarcraftNavigationReader().Read(m.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, m.Nodes["JP"]);
        Assert.Equal(expected, result.Status);
        if (expected is NativeNavigationStatus.Conflict or NativeNavigationStatus.Unselected) Assert.Null(result.Resolve("manual"));
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(3)]
    [InlineData(4)][InlineData(7)]
    public void OeCountsCumulativeFailuresWithoutClampingOrInventingAttempts(int count)
    {
        var m = new BulletNativeGlobalsTests.GlobalsMemory(); m.Array("OE", 9, count); m.Array("cr", 13, 1);
        var quests = RouteQuestSnapshot.FromVerifiedSlots([new(0, "Q006", false), new(1, "Q001", true), new(2, "Q002", true)]);
        var result = new WarcraftHighGambleReader().Read(m.Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0, m.Nodes["JP"], quests);
        Assert.True(result.IsVerified); Assert.Equal(count, result.Failures);
        Assert.True(result.Active);
        Assert.Equal(HighGamblePresentation.Describe(new(true, true, count, "")), HighGamblePresentation.Describe(result));
    }
    [Theory]
    [InlineData("missing")][InlineData("negative")][InlineData("type")][InlineData("owner")][InlineData("assignment")]
    [InlineData("changed")]
    public void InvalidOeIsUnknownNotZero(string fault)
    {
        var m = new BulletNativeGlobalsTests.GlobalsMemory(); m.Array("OE", 9, fault == "negative" ? -1 : 2); m.Array("cr", 13, 1);
        if (fault == "missing") m.Put(m.Nodes["OE"] + 56, 0UL);
        if (fault == "type") m.Put(m.Nodes["OE"] + 48, 13);
        var quests = RouteQuestSnapshot.FromVerifiedSlots([new(0, "Q006", fault == "assignment"), new(1, "Q001", true), new(2, "Q002", true)]);
        var changed = false;
        byte[] Read(ulong address, int size) { var b = m.Read(address, size);
            if (!changed && fault == "changed" && address == m.Data("cr")) { changed = true; m.Put(m.Data("OE"), 3); } return b; }
        var result = new WarcraftHighGambleReader().Read(Read, "2.0.4.23745", RouteQuestCatalog.MapScriptSha256,
            fault == "owner" ? null : (byte?)0, m.Nodes["JP"], quests);
        Assert.False(result.IsVerified); Assert.Null(result.Failures);
    }
    [Theory]
    [InlineData("rawcode")][InlineData("owner")][InlineData("self")][InlineData("vtable")]
    [InlineData("dead")][InlineData("nan")][InlineData("lifeHandle")][InlineData("moving")][InlineData("map")]
    public void InvalidOrChangedMarkerCannotActivateEffects(string fault)
    {
        var m = new BulletGoroseiNativeTests.MarkerMemory(); const ulong unit = BulletGoroseiNativeTests.MarkerMemory.Unit;
        if (fault == "rawcode") m.U32(unit + 0x178, 0x6f303245);
        if (fault == "owner") m.Put(unit + 0x1c0, [0]);
        if (fault == "self") m.U64(0x140090, unit + 0x1000);
        if (fault == "vtable") m.U64(unit, 0);
        if (fault == "dead") m.Put(0x1410d0, BitConverter.GetBytes(0f));
        if (fault == "nan") m.Put(0x1410d0, BitConverter.GetBytes(float.NaN));
        if (fault == "lifeHandle") m.U64(unit + 0x258, ulong.MaxValue);
        var changed = false;
        byte[] Read(ulong address, int size) { var b = m.Read(address, size);
            if (!changed && fault == "moving" && address == 0x1410c8) { changed = true; m.Put(unit + 0x1c0, [0]); } return b; }
        var result = m.Reader(Read).Read("2.0.4.23745", fault == "map" ? "wrong" : RouteQuestCatalog.MapScriptSha256, [(unit, 0x6f303332)]);
        Assert.Equal(GoroseiMarkerStatus.Unknown, result.Status); Assert.False(result.EffectsActiveVerified);
    }
    [Fact]
    public void DuplicatePointerDeduplicatesButSameModeDistinctIdentityIsUnknown()
    {
        var m = new BulletGoroseiNativeTests.MarkerMemory(); const ulong unit = BulletGoroseiNativeTests.MarkerMemory.Unit;
        var duplicate = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, [(unit, 0x6f303332), (unit, 0x6f303332)]);
        Assert.Equal(GoroseiMarkerStatus.SelectedIdentity, duplicate.Status); Assert.Single(duplicate.Candidates);
        m.Second(0x6f303332);
        var ambiguous = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, [(unit, 0x6f303332), (0x110000, 0x6f303332)]);
        Assert.Equal(GoroseiMarkerStatus.Unknown, ambiguous.Status); Assert.Equal(2, ambiguous.Candidates.Length);
    }
    [Fact]
    public void MarkerSessionRejectsOldGenerationRevisionAndDoesNotReuseLastKnownOnReset()
    {
        var m = new BulletGoroseiNativeTests.MarkerMemory();
        var marker = m.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, [(BulletGoroseiNativeTests.MarkerMemory.Unit, 0x6f303332)]);
        var s = new GoroseiObservationSession(); s.Reset(1); s.Accept(1, 2, marker, true);
        s.Accept(1, 1, GoroseiMarkerSnapshot.Unknown, true); Assert.True(s.Current.IsCurrent);
        s.Accept(1, 3, marker, false); Assert.False(s.Current.IsCurrent); Assert.Equal(GoroseiMode.None, s.Current.EffectMode);
        s.Reset(2); s.Accept(1, 9, marker, true); Assert.False(s.Current.IsCurrent); Assert.Equal(GoroseiMode.None, s.LastKnown);
    }
}
