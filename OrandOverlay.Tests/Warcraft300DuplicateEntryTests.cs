using Xunit;

namespace OrandOverlay.Tests;

public sealed partial class Warcraft300DiagnosticTests
{
    private const ulong SecondRegistered = Fixture.Unit + 0x2000;

    private static Fixture WithRegisteredAliases(bool secondUnit = false)
    {
        var f = new Fixture();
        f.D(Fixture.Frame + 0xC08, 3);
        f.Q(Fixture.Array + 8, Fixture.Unit);
        f.Q(Fixture.Array + 16, secondUnit ? SecondRegistered : Fixture.Unit);
        if (secondUnit)
        {
            f.Q(SecondRegistered, Fixture.B + 0x2792E78);
            f.D(SecondRegistered + 0x18, 1); f.D(SecondRegistered + 0x1C, 7);
            f.D(SecondRegistered + 0x1C0, 6); f.D(SecondRegistered + 0x178, 0x68303031);
            f.D(0x260000030, 2);
            f.D(0x270000010, 0xFFFFFFFE); f.Q(0x270000018, 0x280001000);
            f.D(0x280001018, 0x2B61676C); f.D(0x280001024, 7);
            f.Q(0x280001090, SecondRegistered);
        }
        return f;
    }

    [Theory]
    [InlineData(6U)]
    [InlineData(27U)]
    public void StableRegisteredAliasesValidateEverySlotButCountOnePhysicalUnit(uint owner)
    {
        var f = WithRegisteredAliases(); f.D(Fixture.Unit + 0x1C0, owner);
        var allocationReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == 0x280000090) allocationReads++;
            return f.Read(address, size);
        }
        var result = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile());

        Assert.Equal(3, result.Count);
        Assert.Equal(owner == 6 ? 1 : 0, result.Owned);
        Assert.Equal(owner == 6 ? 0 : 1, result.Foreign);
        Assert.Equal(owner == 6 ? 1 : 0, result.Rawcodes.GetValueOrDefault(0x68303031U));
        Assert.Equal(Fixture.Unit, Assert.Single(result.Units).Address);
        Assert.Equal(new[] { Fixture.Unit, Fixture.Unit, Fixture.Unit }, result.EntryAddresses);
        Assert.Equal(4, allocationReads); // Each slot plus the closing unique-unit stamp.
        Assert.Throws<NotSupportedException>(() => ((IList<ulong>)result.EntryAddresses)[0] = 0);
    }

    [Fact]
    public void SeparatePhysicalUnitsWithTheSameRawcodeAreNotDeduplicated()
    {
        var result = WithRegisteredAliases(secondUnit: true).Run();
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Owned);
        Assert.Equal(2, result.Units.Count);
        Assert.Equal(2, Assert.Single(result.Rawcodes).Value);
    }

    [Theory]
    [InlineData(6U)]
    [InlineData(27U)]
    public void GrowthProjectionUsesTheSingleValidatedPhysicalUnitWhenFrameSlotsAlias(uint owner)
    {
        var f = WithRegisteredAliases(); f.D(Fixture.Unit + 0x1C0, owner);
        var inventory = f.Run(); var unit = Assert.Single(inventory.Units);
        var projected = GrowthMaterialInventory.Project(inventory, inventory.CurrentView,
            unit.Address, unit.Rawcode, unit.Allocation, _ => true);
        Assert.Equal(owner == 27 ? 1 : 0, projected.AddedObjects);
        Assert.Equal(1, Assert.Single(projected.Rawcodes).Value);
        Assert.Equal(0, inventory.Foreign - projected.AddedObjects);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("record")]
    [InlineData("registry-state")]
    [InlineData("owner")]
    [InlineData("registration")]
    [InlineData("vtable")]
    [InlineData("allocation")]
    public void AliasedIdentityOwnershipOrAllocationMutationNeverRetries(string mutation)
    {
        var f = WithRegisteredAliases();
        var vtableReads = 0; var vectorReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Array) vectorReads++;
            // First slot performs identity + registry reads; mutate at the second slot.
            if (address == Fixture.Unit && ++vtableReads == 3)
            {
                if (mutation == "generation") { f.D(Fixture.Unit + 0x1C, 6); f.D(0x280000024, 6); }
                if (mutation == "record")
                {
                    f.Put(0x280001000, f.Read(0x280000000, 0x98));
                    f.Q(0x270000008, 0x280001000);
                }
                if (mutation == "registry-state") f.Put(0x280000083, [2]);
                if (mutation == "owner") f.D(Fixture.Unit + 0x1C0, 7);
                if (mutation == "registration")
                { f.D(Fixture.Unit + 0x18, uint.MaxValue); f.D(Fixture.Unit + 0x1C, uint.MaxValue); }
                if (mutation == "vtable") f.Q(Fixture.Unit, 0);
                if (mutation == "allocation") f.D(0x270000000, 0);
            }
            return f.Read(address, size);
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(1, vectorReads);
    }

    [Fact]
    public void AliasMustPassClosingAllocationFenceAfterAllSlots()
    {
        var f = WithRegisteredAliases(); var vtableReads = 0; var vectorReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Array) vectorReads++;
            if (address == Fixture.Unit && ++vtableReads == 7)
            { f.D(Fixture.Unit + 0x1C, 6); f.D(0x280000024, 6); }
            return f.Read(address, size);
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(1, vectorReads);
    }

    [Fact]
    public void MixedUnregisteredAndRegisteredAliasesPreserveSlotEvidenceWithoutExtraCounts()
    {
        var f = WithUnregisteredEntry(duplicate: true);
        f.D(Fixture.Frame + 0xC08, 4); f.Q(Fixture.Array + 24, Fixture.Unit);
        var result = f.Run();
        Assert.Equal(4, result.Count); Assert.Equal(1, result.Owned); Assert.Equal(0, result.Foreign);
        Assert.Single(result.Units); Assert.Equal(2, result.UnregisteredEntries.Count);
        Assert.Equal(new[] { Unregistered, Fixture.Unit, Unregistered, Fixture.Unit }, result.EntryAddresses);
        Assert.Equal(1, Assert.Single(result.Rawcodes).Value);
    }

    [Fact]
    public void UnregisteredAliasCannotBecomeRegisteredBetweenSlots()
    {
        var f = WithRegisteredAliases();
        f.D(Fixture.Unit + 0x18, uint.MaxValue); f.D(Fixture.Unit + 0x1C, uint.MaxValue);
        var vtableReads = 0; var vectorReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Array) vectorReads++;
            if (address == Fixture.Unit && ++vtableReads == 2)
            { f.D(Fixture.Unit + 0x18, 0); f.D(Fixture.Unit + 0x1C, 5); }
            return f.Read(address, size);
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        Assert.Equal(1, vectorReads);
    }

    [Fact]
    public void RawcodeChangeBetweenAliasesRetriesTheWholeSnapshotOnce()
    {
        var f = WithRegisteredAliases(); var rawcodeReads = 0; var vectorReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Array) vectorReads++;
            if (address == Fixture.Unit + 0x178 && ++rawcodeReads == 2) f.D(address, ChangedRawcode);
            return f.Read(address, size);
        }
        var result = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile());
        Assert.Equal(4, vectorReads); Assert.Equal(8, rawcodeReads);
        Assert.Equal(ChangedRawcode, Assert.Single(result.Rawcodes).Key);
        Assert.Equal(1, Assert.Single(result.Rawcodes).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactAliasVectorIsCheckedAndOnlyOneSameContextRetryIsAllowed(bool persistent)
    {
        var f = WithRegisteredAliases(secondUnit: true); var vectorReads = 0;
        byte[] Read(ulong address, int size)
        {
            if (address == Fixture.Array && ++vectorReads % 2 == 0 && (persistent || vectorReads == 2))
            {
                var atSecond = BitConverter.ToUInt64(f.Read(Fixture.Array + 8, 8));
                f.Q(Fixture.Array + 8, atSecond == Fixture.Unit ? SecondRegistered : Fixture.Unit);
                f.Q(Fixture.Array + 16, atSecond);
            }
            return f.Read(address, size);
        }
        if (persistent)
            Assert.Throws<InvalidDataException>(() => Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile()));
        else
        {
            var result = Warcraft300Diagnostic.ReadInventory(Read, Fixture.B, Fixture.Frame, Profile());
            Assert.Equal(new[] { Fixture.Unit, SecondRegistered, Fixture.Unit }, result.EntryAddresses);
            Assert.Equal(2, result.Owned);
        }
        Assert.Equal(4, vectorReads);
    }

    [Fact]
    public void OuterBindingPreservesAliasSlotOrderEvenWhenUniqueUnitsAreUnchanged()
    {
        var f = WithRegisteredAliases(secondUnit: true); var before = f.Run();
        f.Q(Fixture.Array + 8, SecondRegistered); f.Q(Fixture.Array + 16, Fixture.Unit);
        var after = f.Run();
        Assert.Equal(before.Units, after.Units);
        Assert.Equal(before.Rawcodes, after.Rawcodes);
        Assert.False(before.SameWorldEntries(after));
        Assert.NotEqual(DiagnosticInventoryBinding.World("salt", before), DiagnosticInventoryBinding.World("salt", after));
        Assert.True(after.SameWorldEntries(f.Run()));
    }

    [Fact]
    public void CancellationDuringAliasValidationStopsImmediately()
    {
        var f = WithRegisteredAliases(); using var cancellation = new CancellationTokenSource();
        var vtableReads = 0;
        byte[] Read(ulong address, int size)
        {
            Assert.False(cancellation.IsCancellationRequested);
            if (address == Fixture.Unit && ++vtableReads == 3) cancellation.Cancel();
            return f.Read(address, size);
        }
        Assert.Throws<OperationCanceledException>(() => Warcraft300Diagnostic.ReadInventory(
            Read, Fixture.B, Fixture.Frame, Profile(), cancellation.Token));
        Assert.Equal(3, vtableReads);
    }
}
