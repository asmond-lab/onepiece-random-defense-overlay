using Xunit;

namespace OrandOverlay.Tests;

public sealed partial class Warcraft300DiagnosticTests
{
    private const ulong Unregistered = Fixture.Unit + 0x1000;

    private static Fixture WithUnregisteredEntry(bool duplicate = false)
    {
        var f = new Fixture();
        f.D(Fixture.Frame + 0xC08, duplicate ? 3U : 2U);
        f.Q(Fixture.Array, Unregistered);
        f.Q(Fixture.Array + 8, Fixture.Unit);
        if (duplicate) f.Q(Fixture.Array + 16, Unregistered);
        f.Q(Unregistered, Fixture.B + 0x2792E78);
        f.D(Unregistered + 0x18, uint.MaxValue);
        f.D(Unregistered + 0x1C, uint.MaxValue);
        return f;
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void StableUnregisteredEntriesDoNotDisconnectOtherValidatedCards(bool duplicate)
    {
        var f = WithUnregisteredEntry(duplicate);
        byte[] Read(ulong a, int n)
        {
            Assert.False(a == Unregistered + 0x178 || a == Unregistered + 0x1C0,
                "Unregistered entries must never contribute rawcode or owner values.");
            return f.Read(a, n);
        }
        var result = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile());
        Assert.Equal(duplicate ? 3 : 2, result.Count);
        Assert.Equal(1, result.Owned);
        Assert.Equal(0, result.Foreign);
        Assert.Equal(Fixture.Unit, Assert.Single(result.Units).Address);
        Assert.Equal(1, result.Rawcodes[0x68303031]);
    }

    [Theory] [InlineData("handle")] [InlineData("serial")] [InlineData("vtable")]
    public void OnlyExactDoubleSentinelCanBeExcluded(string field)
    {
        var f = WithUnregisteredEntry();
        if (field == "handle") f.D(Unregistered + 0x18, 0);
        if (field == "serial") f.D(Unregistered + 0x1C, 0);
        if (field == "vtable") f.Q(Unregistered, Fixture.B);
        Assert.Throws<InvalidDataException>(() => f.Run());
    }

    [Theory] [InlineData("handle")] [InlineData("serial")] [InlineData("vtable")]
    public void UnregisteredEntryChangingDuringReadRejectsSnapshot(string field)
    {
        var f = WithUnregisteredEntry(); var seen = 0;
        ulong address = Unregistered + (field == "handle" ? 0x18UL : field == "serial" ? 0x1CUL : 0);
        byte[] Read(ulong a, int n)
        {
            var data = f.Read(a, n);
            if (a == address && ++seen == 2) data[0] ^= 1;
            return data;
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
    }

    [Fact]
    public void MovingUnregisteredEntryAcrossOuterBracketChangesSnapshot()
    {
        var f = WithUnregisteredEntry(); var before = f.Run();
        f.Q(Fixture.Array, Fixture.Unit); f.Q(Fixture.Array + 8, Unregistered);
        var after = f.Run();
        Assert.Equal(before.Units, after.Units);
        Assert.False(before.SameWorldEntries(after));
        Assert.True(after.SameWorldEntries(f.Run()));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ShortUnregisteredIdentityReadCannotBecomeExclusion(int field)
    {
        var f = WithUnregisteredEntry();
        ulong address = Unregistered + (field == 0 ? 0UL : field == 1 ? 0x18UL : 0x1CUL);
        byte[] Read(ulong a, int n) => a == address ? new byte[n - 1] : f.Read(a, n);
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CancellationDuringUnregisteredIdentityDoesNotReturnInventory(int field)
    {
        var f = WithUnregisteredEntry(); using var cancellation = new CancellationTokenSource();
        byte[] Read(ulong a, int n)
        {
            Assert.False(cancellation.IsCancellationRequested, "No read may follow cancellation");
            var data = f.Read(a, n);
            if (a == Unregistered + (field == 0 ? 0UL : field == 1 ? 0x18UL : 0x1CUL)) cancellation.Cancel();
            return data;
        }
        Assert.Throws<OperationCanceledException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile(), cancellation.Token));
    }

    [Theory] [InlineData(28UL)] [InlineData(29UL)] [InlineData(30UL)]
    public void UnregisteredHeaderMustFitUserAddressSpaceBeforeAnyIdentityRead(ulong distance)
    {
        var f = new Fixture(); var address = 0x7FFFFFFFFFFFUL - distance;
        f.Q(Fixture.Array, address); f.Q(address, Fixture.B + 0x2792E78);
        f.D(address + 0x18, uint.MaxValue); f.D(address + 0x1C, uint.MaxValue);
        var identityReads = 0;
        byte[] Read(ulong a, int n)
        {
            if (a >= address) identityReads++;
            return f.Read(a, n);
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(0, identityReads);
    }

    [Fact]
    public void InvalidAllocatedUnitStillDisconnectsAndLaterValidReadRecovers()
    {
        var f = WithUnregisteredEntry();
        f.D(0x270000000, 0);
        Assert.Throws<InvalidDataException>(() => f.Run());
        f.D(0x270000000, 0xFFFFFFFE);
        Assert.Equal(1, f.Run().Owned);
    }
}
