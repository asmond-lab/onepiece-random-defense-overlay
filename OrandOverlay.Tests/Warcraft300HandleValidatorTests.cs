using Xunit;

namespace OrandOverlay.Tests;

public sealed class Warcraft300HandleValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryAndSecondaryHaveExactUnsignedAbi(bool alternate)
    {
        var memory = new Memory(alternate);
        var stamp = memory.Validate();
        Assert.Equal(Memory.Module, stamp.ModuleBase);
        Assert.Equal(Memory.Unit, stamp.Unit);
        Assert.Equal(Memory.Module + 0x2792E78, stamp.Vtable);
        Assert.Equal(Memory.Module + 0x2F807F0, stamp.RegistryGlobal);
        Assert.Equal(Memory.Registry, stamp.Registry);
        Assert.Equal(alternate ? 0x80000003U : 3U, stamp.RawHandle);
        Assert.Equal(0xF1234567U, stamp.Serial);
        Assert.Equal(stamp.Serial, stamp.RecordSerial);
        Assert.Equal(alternate, stamp.Alternate);
        Assert.Equal(3U, stamp.Index);
        Assert.Equal(Memory.Table, stamp.Table);
        Assert.Equal(4U, stamp.TableLimit);
        Assert.Equal(Memory.Table + 48, stamp.Slot);
        Assert.Equal(0xFFFFFFFEU, stamp.Marker);
        Assert.Equal(Memory.Record, stamp.Record);
        Assert.Equal(0x2B61676CU, stamp.TypeId);
        Assert.Equal(Memory.Unit, stamp.BackReference);
        Assert.Equal(0UL, stamp.State30);
        Assert.Equal((byte)0xA4, stamp.State83);
        Assert.Equal(stamp, memory.Validate());
        Assert.Contains((Memory.Unit + 0x18, 4), memory.Calls);
        Assert.Contains((Memory.Unit + 0x1C, 4), memory.Calls);
        Assert.Contains((Memory.Record + 0x18, 4), memory.Calls);
        Assert.Contains((Memory.Record + 0x24, 4), memory.Calls);
        Assert.All(memory.Calls, call => Assert.Contains(call.Length, new[] { 1, 4, 8 }));
        Assert.DoesNotContain(memory.Calls, c => c.Address == Memory.Registry + (alternate ? 0x18UL : 0x50UL));
        Assert.Equal(13, memory.Calls.Count / 2);
    }

    [Theory]
    [InlineData("serial")]
    [InlineData("marker")]
    [InlineData("type")]
    [InlineData("backref")]
    [InlineData("state30")]
    [InlineData("state83")]
    [InlineData("vtable")]
    public void RejectsMismatches(string field)
    {
        var memory = new Memory();
        switch (field)
        {
            case "serial": memory.Put32(Memory.Record + 0x24, 7); break;
            case "marker": memory.Put32(Memory.Table + 48, uint.MaxValue); break;
            case "type": memory.Put32(Memory.Record + 0x18, 0x6C67612B); break;
            case "backref": memory.Put64(Memory.Record + 0x90, Memory.Unit + 8); break;
            case "state30": memory.Put64(Memory.Record + 0x30, 0x100000000); break;
            case "state83": memory.Put8(Memory.Record + 0x83, 0xA5); break;
            case "vtable": memory.Put64(Memory.Unit, Memory.Module + 0x2792E80); break;
        }
        Assert.Throws<InvalidDataException>(() => memory.Validate());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecycledSameAddressRequiresMatchingNewSerialAndChangesStamp(bool alternate)
    {
        var memory = new Memory(alternate);
        var before = memory.Validate();
        memory.Put32(Memory.Record + 0x24, 0xF1234568);
        Assert.Throws<InvalidDataException>(() => memory.Validate());
        memory.Put32(Memory.Unit + 0x1C, 0xF1234568);
        var after = memory.Validate();
        Assert.Equal(before.Unit, after.Unit);
        Assert.Equal(before.Record, after.Record);
        Assert.NotEqual(before, after);
    }

    [Theory]
    [InlineData("registry")]
    [InlineData("table")]
    [InlineData("record")]
    [InlineData("limit")]
    [InlineData("handle")]
    [InlineData("state83")]
    public void AcceptedChangesProduceDifferentComparisonStamps(string field)
    {
        var memory = new Memory();
        var before = memory.Validate();
        switch (field)
        {
            case "registry":
                memory.Put64(Memory.Module + 0x2F807F0, Memory.Registry + 0x1000);
                memory.Put64(Memory.Registry + 0x1018, Memory.Table);
                memory.Put32(Memory.Registry + 0x1030, 4);
                break;
            case "table":
                memory.Put64(Memory.Registry + 0x18, Memory.Table + 0x1000);
                memory.Put32(Memory.Table + 0x1030, 0xFFFFFFFE);
                memory.Put64(Memory.Table + 0x1038, Memory.Record);
                break;
            case "record":
                memory.Put64(Memory.Table + 56, Memory.Record + 0x1000);
                memory.SeedRecord(Memory.Record + 0x1000);
                break;
            case "limit": memory.Put32(Memory.Registry + 0x30, 5); break;
            case "handle":
                memory.Put32(Memory.Unit + 0x18, 2);
                memory.Put32(Memory.Table + 32, 0xFFFFFFFE);
                memory.Put64(Memory.Table + 40, Memory.Record);
                break;
            case "state83": memory.Put8(Memory.Record + 0x83, 0xA6); break;
        }
        Assert.NotEqual(before, memory.Validate());
        Assert.Equal((byte)0xA4, before.State83);
    }

    [Theory]
    [InlineData(0U, 0U, false)]
    [InlineData(4U, 4U, false)]
    [InlineData(4U, 5U, false)]
    [InlineData(16777217U, 3U, false)]
    [InlineData(4294967295U, 3U, false)]
    [InlineData(16777216U, 16777216U, false)]
    [InlineData(16777216U, 2147483647U, false)]
    [InlineData(1U, 0U, true)]
    [InlineData(16777216U, 16777215U, true)]
    public void SelectedTableLimitsAreUnsignedAndBounded(uint limit, uint index, bool accepted)
    {
        foreach (var alternate in new[] { false, true })
        {
            var memory = new Memory(alternate);
            memory.Put32(Memory.Unit + 0x18, index | (alternate ? 0x80000000U : 0));
            memory.Put32(Memory.Registry + (alternate ? 0x68UL : 0x30UL), limit);
            var slot = Memory.Table + 16UL * index;
            memory.Put32(slot, 0xFFFFFFFE);
            memory.Put64(slot + 8, Memory.Record);
            if (accepted) Assert.Equal(index, memory.Validate().Index);
            else
            {
                Assert.Throws<InvalidDataException>(() => memory.Validate());
                Assert.DoesNotContain(memory.Calls, c => c.Address == slot);
            }
        }
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(65535UL)]
    [InlineData(0x800000000000UL)]
    [InlineData(ulong.MaxValue)]
    public void ImplausibleRootsAndPointersAreRejectedBeforeDereference(ulong bad)
    {
        var memory = new Memory();
        Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(memory.Read, bad, Memory.Unit, default));
        Assert.Empty(memory.Calls);
        Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(memory.Read, Memory.Module, bad, default));
        Assert.Empty(memory.Calls);
        foreach (var pointerField in new[] { Memory.Module + 0x2F807F0, Memory.Registry + 0x18, Memory.Table + 56 })
        {
            memory = new Memory();
            memory.Put64(pointerField, bad);
            Assert.Throws<InvalidDataException>(() => memory.Validate());
            Assert.DoesNotContain(memory.Calls, c => c.Address == bad);
        }
    }

    [Theory]
    [InlineData("module")]
    [InlineData("unit")]
    [InlineData("registry")]
    [InlineData("table")]
    [InlineData("record")]
    public void CompleteSpansMustFitCanonicalUserAddressSpace(string field)
    {
        const ulong top = 0x7FFFFFFFFFFF;
        var memory = new Memory();
        var module = Memory.Module;
        var unit = Memory.Unit;
        switch (field)
        {
            case "module": module = top - 0x10; break;
            case "unit": unit = top - 0x10; break;
            case "registry": memory.Put64(Memory.Module + 0x2F807F0, top - 0x10); break;
            // Selected first slot would fit, but the declared full table span would not.
            case "table":
                memory.Put32(Memory.Unit + 0x18, 0);
                memory.Put64(Memory.Registry + 0x18, top - 31);
                break;
            case "record": memory.Put64(Memory.Table + 56, top - 0x90); break;
        }
        Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(memory.Read, module, unit, default));
        Assert.All(memory.Calls, c => Assert.True(c.Address + (ulong)c.Length - 1 <= top));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void EveryReadRejectsNullShortOrOversizedResponses(int change)
    {
        for (var failAt = 1; failAt <= 13; failAt++)
        {
            var memory = new Memory();
            var reads = 0;
            byte[] Read(ulong address, int length)
            {
                reads++;
                if (reads == failAt) return change == -1 ? null! : new byte[length + (change == 0 ? -1 : 1)];
                return memory.Read(address, length);
            }
            Assert.Throws<InvalidDataException>(() => Warcraft300HandleValidator.Read(Read, Memory.Module, Memory.Unit, default));
            Assert.Equal(failAt, reads);
        }
    }

    [Fact]
    public void CancellationBeforeReadDoesNotCallDelegate()
    {
        var memory = new Memory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Warcraft300HandleValidator.Read(memory.Read, Memory.Module, Memory.Unit, cancellation.Token));
        Assert.Empty(memory.Calls);
    }

    [Fact]
    public void CancellationDuringEveryReadIncludingLastPropagatesWithoutStamp()
    {
        for (var cancelAt = 1; cancelAt <= 13; cancelAt++)
        {
            var memory = new Memory();
            using var cancellation = new CancellationTokenSource();
            byte[] Read(ulong address, int length)
            {
                var bytes = memory.Read(address, length);
                if (memory.Calls.Count == cancelAt) cancellation.Cancel();
                return bytes;
            }
            var error = Assert.Throws<OperationCanceledException>(() => Warcraft300HandleValidator.Read(Read, Memory.Module, Memory.Unit, cancellation.Token));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Equal(cancelAt, memory.Calls.Count);
        }
    }

    [Fact]
    public void ReaderFailuresPropagateAndNullDelegateIsRejected()
    {
        var error = new IOException("synthetic read failure");
        Assert.Same(error, Assert.Throws<IOException>(() => Warcraft300HandleValidator.Read((_, _) => throw error, Memory.Module, Memory.Unit, default)));
        Assert.Throws<ArgumentNullException>(() => Warcraft300HandleValidator.Read(null!, Memory.Module, Memory.Unit, default));
    }

    private sealed class Memory
    {
        internal const ulong Module = 0x140000000;
        internal const ulong Registry = 0x200000;
        internal const ulong Table = 0x300000;
        internal const ulong Record = 0x400000;
        internal const ulong Unit = 0x500000;
        private readonly Dictionary<ulong, byte> _bytes = new();
        internal List<(ulong Address, int Length)> Calls { get; } = new();

        internal Memory(bool alternate = false)
        {
            Put64(Module + 0x2F807F0, Registry);
            Put64(Unit, Module + 0x2792E78);
            Put32(Unit + 0x18, alternate ? 0x80000003U : 3U);
            Put32(Unit + 0x1C, 0xF1234567);
            Put64(Registry + (alternate ? 0x50UL : 0x18UL), Table);
            Put32(Registry + (alternate ? 0x68UL : 0x30UL), 4);
            Put32(Table + 48, 0xFFFFFFFE);
            Put64(Table + 56, Record);
            SeedRecord(Record);
        }

        internal void SeedRecord(ulong record)
        {
            Put32(record + 0x24, 0xF1234567);
            Put32(record + 0x18, 0x2B61676C);
            Put64(record + 0x90, Unit);
            Put64(record + 0x30, 0);
            Put8(record + 0x83, 0xA4);
        }

        internal Warcraft300HandleStamp Validate() => Warcraft300HandleValidator.Read(Read, Module, Unit, default);
        internal byte[] Read(ulong address, int length)
        {
            Calls.Add((address, length));
            // Missing bytes fail the fixture: any extra read, fallback, or wrong ABI is visible.
            return Enumerable.Range(0, length).Select(i => _bytes[checked(address + (ulong)i)]).ToArray();
        }
        internal void Put8(ulong address, byte value) => _bytes[address] = value;
        internal void Put32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void Put64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        private void Put(ulong address, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++) _bytes[checked(address + (ulong)i)] = bytes[i];
        }
    }
}
