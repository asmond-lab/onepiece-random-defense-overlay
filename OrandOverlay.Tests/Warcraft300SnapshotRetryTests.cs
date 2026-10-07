using Xunit;

namespace OrandOverlay.Tests;

public sealed partial class Warcraft300DiagnosticTests
{
    private const uint ChangedRawcode = 0x68303032;

    [Fact]
    public void TransientRawcodeMutationRetriesFromFreshInventory()
    {
        var f = new Fixture();
        var rawcodeReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads == 2)
                f.D(address, ChangedRawcode);
            return f.Read(address, size);
        }

        var result = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile());

        Assert.Equal(4, rawcodeReads);
        Assert.Equal(ChangedRawcode, Assert.Single(result.Rawcodes).Key);
        Assert.Equal(ChangedRawcode, Assert.Single(result.Units).Rawcode);
    }

    [Theory]
    [InlineData("vector")]
    [InlineData("count")]
    [InlineData("entries")]
    public void TransientVectorMutationRetriesEveryUnitAndReturnsFreshInventory(string mutation)
    {
        var f = WithUnregisteredEntry();
        var observedAddress = mutation == "vector" ? Fixture.Array :
            Fixture.Frame + (mutation == "count" ? 0xC08UL : 0xC10UL);
        var reads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == observedAddress && ++reads == 2)
            {
                if (mutation == "vector")
                {
                    f.Q(Fixture.Array, Fixture.Unit);
                    f.Q(Fixture.Array + 8, Unregistered);
                }
                else if (mutation == "count")
                {
                    f.D(Fixture.Frame + 0xC08, 1);
                    f.Q(Fixture.Array, Fixture.Unit);
                }
                else
                {
                    f.Q(Fixture.Frame + 0xC10, Fixture.Array + 0x1000);
                    f.Q(Fixture.Array + 0x1000, Fixture.Unit);
                    f.Q(Fixture.Array + 0x1008, Unregistered);
                }
                f.D(Fixture.Unit + 0x178, ChangedRawcode);
            }
            return f.Read(address, size);
        }

        var result = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile());

        Assert.Equal(4, reads);
        Assert.Equal(ChangedRawcode, Assert.Single(result.Rawcodes).Key);
        Assert.Equal(1, result.Owned);
        Assert.Equal(mutation == "count" ? 1 : 2, result.Count);
        if (mutation == "count") Assert.Empty(result.UnregisteredEntries);
        else Assert.Equal(1, Assert.Single(result.UnregisteredEntries).Index);
    }

    [Fact]
    public void PersistentRawcodeMutationFailsAfterExactlyOneRetry()
    {
        var f = new Fixture();
        var rawcodeReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads % 2 == 0)
                f.D(address, ChangedRawcode + (uint)rawcodeReads);
            return f.Read(address, size);
        }

        Assert.Throws<InvalidDataException>(() =>
            Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(4, rawcodeReads);
    }

    [Theory]
    [InlineData("pointer")]
    [InlineData("vtable")]
    [InlineData("owner")]
    [InlineData("owner-change")]
    [InlineData("registry")]
    [InlineData("generation")]
    [InlineData("count")]
    [InlineData("entries")]
    public void StructuralAndIdentityFailuresNeverRetry(string failure)
    {
        var f = new Fixture();
        if (failure == "pointer") f.Q(Fixture.Array, 0);
        if (failure == "vtable") f.Q(Fixture.Unit, 0);
        if (failure == "owner") f.D(Fixture.Unit + 0x1C0, 28);
        if (failure == "registry") f.D(0x270000000, 0);
        var vectorReads = 0;
        var ownerReads = 0;
        var handleReads = 0;
        var closingFieldReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Array) vectorReads++;
            if (failure == "owner-change" && address == Fixture.Unit + 0x1C0 && ++ownerReads == 2)
                f.D(address, 7);
            if (failure == "generation" && address == Fixture.Unit + 0x18 && ++handleReads == 2)
            {
                f.D(Fixture.Unit + 0x1C, 6);
                f.D(0x280000024, 6);
            }
            if (failure == "count" && address == Fixture.Frame + 0xC08 && ++closingFieldReads == 2)
                f.D(address, 0);
            if (failure == "entries" && address == Fixture.Frame + 0xC10 && ++closingFieldReads == 2)
                f.Q(address, 0);
            return f.Read(address, size);
        }

        Assert.Throws<InvalidDataException>(() =>
            Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(1, vectorReads);
    }

    [Fact]
    public void InvalidProfileFailsBeforeAnyRead()
    {
        byte[] Read(ulong address, int size) => throw new Xunit.Sdk.XunitException("Invalid profile must not read.");
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(
            Read, Fixture.B, Fixture.Frame, Profile("3.0.0.24268=3.0.0.24267")));
    }

    [Theory]
    [InlineData("view")]
    [InlineData("world")]
    public void MutationAndContextChangeNeverRetry(string change)
    {
        var f = new Fixture();
        var rawcodeReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads == 2)
            {
                f.D(address, ChangedRawcode);
                ChangeContext(f, change);
            }
            return f.Read(address, size);
        }

        Assert.Throws<InvalidDataException>(() =>
            Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(2, rawcodeReads);
    }

    [Theory]
    [InlineData("view")]
    [InlineData("world")]
    public void RetryCannotCrossContextBoundary(string change)
    {
        var f = new Fixture();
        var rawcodeReads = 0;
        var rootReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads == 2)
                f.D(address, ChangedRawcode);
            // Four root reads bracket attempt one. Change context before attempt two starts.
            if (address == Fixture.B + 0x2E9AD00 && ++rootReads == 5)
                ChangeContext(f, change);
            return f.Read(address, size);
        }

        Assert.Throws<InvalidDataException>(() =>
            Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(2, rawcodeReads);
    }

    private static void ChangeContext(Fixture f, string change)
    {
        if (change == "view")
        {
            f.Put(Fixture.Game + 0x262C, BitConverter.GetBytes((ushort)7));
            f.Q(Fixture.Game + 0x26A0 + 7 * 8, Fixture.Player);
        }
        else
        {
            var nextUi = Fixture.Ui + 0x1000;
            f.Q(Fixture.B + 0x2F5EF00, nextUi);
            f.Q(Fixture.B + 0x2F85360, nextUi);
            f.Q(nextUi, Fixture.B + 0x275ED08);
            f.Q(Fixture.Frame + 0x40, nextUi);
        }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("registration")]
    public void RetryCannotAcceptChangedIdentityOrOwnerAtSameAddress(string change)
    {
        var f = new Fixture();
        var rawcodeReads = 0;
        var rootReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads == 2)
                f.D(address, ChangedRawcode);
            if (address == Fixture.B + 0x2E9AD00 && ++rootReads == 5)
            {
                if (change == "owner") f.D(Fixture.Unit + 0x1C0, 7);
                if (change == "generation")
                {
                    f.D(Fixture.Unit + 0x1C, 6);
                    f.D(0x280000024, 6);
                }
                if (change == "registration")
                {
                    f.D(Fixture.Unit + 0x18, uint.MaxValue);
                    f.D(Fixture.Unit + 0x1C, uint.MaxValue);
                }
            }
            return f.Read(address, size);
        }

        Assert.Throws<InvalidDataException>(() =>
            Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(2, rawcodeReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringMutationOrRetryStopsFurtherReads(bool duringRetry)
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var rawcodeReads = 0;
        var rootReads = 0;
        byte[] Read(ulong address, int size)
        {
            Assert.False(cancellation.IsCancellationRequested, "No read may follow cancellation.");
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads == 2)
            {
                f.D(address, ChangedRawcode);
                if (!duringRetry) cancellation.Cancel();
            }
            if (address == Fixture.B + 0x2E9AD00 && ++rootReads == 5 && duringRetry)
                cancellation.Cancel();
            return f.Read(address, size);
        }

        Assert.Throws<OperationCanceledException>(() =>
            Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile(), cancellation.Token));
        Assert.Equal(2, rawcodeReads);
    }

    [Fact]
    public void AlreadyCancelledSnapshotDoesNotRead()
    {
        byte[] Read(ulong address, int size) => throw new Xunit.Sdk.XunitException("Cancelled snapshot must not read.");
        Assert.Throws<OperationCanceledException>(() => Warcraft300Diagnostic.ReadInventory(
            Read, Fixture.B, Fixture.Frame, Profile(), new CancellationToken(true)));
    }
}
