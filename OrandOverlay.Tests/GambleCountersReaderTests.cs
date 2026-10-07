using System.ComponentModel;
using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class GambleCountersReaderTests
{
    private const string Version = "2.0.4.23745";
    private const string MapHash = "0bccc47907a9505f38efaf6bbf20228a728eabdfaec3209cca7df2269bfc2028";

    [Fact]
    public void ReadsSixTypesFromTheirOwnAttemptAndResultCountersForConfirmedOwner()
    {
        var memory = new GlobalsMemory();
        var snapshot = Read(memory);
        Assert.NotNull(snapshot);
        Assert.Equal((byte)1, snapshot.LocalSlot);
        Assert.Equal(Version, snapshot.WarcraftVersion);
        Assert.Equal(MapHash, snapshot.MapScriptSha256);
        Assert.Equal(6, snapshot.Counters.Length);
        AssertTotals(snapshot, GambleCounterKind.Low, 10, 7, 3);
        AssertTotals(snapshot, GambleCounterKind.Middle, 8, 5, 3);
        AssertTotals(snapshot, GambleCounterKind.High, 12, 8, 4);
        AssertTotals(snapshot, GambleCounterKind.World, 9, 2, 7);
        AssertTotals(snapshot, GambleCounterKind.Absalom, 6, 1, 5);
        AssertTotals(snapshot, GambleCounterKind.LumberWisp, 11, 7, 4);
        Assert.Equal(4, snapshot.SourceCounters["OE"]);
        Assert.Equal(12, snapshot.SourceCounters["kE"]);
        Assert.Equal(2, snapshot.SourceCounters["ZE"]);
        Assert.Equal(6, snapshot.SourceCounters["nE"]);
        Assert.Equal(0, memory.QuestReads);
    }

    [Theory]
    [InlineData(GambleCounterKind.Low, "h06B", 200, 1)]
    [InlineData(GambleCounterKind.Middle, "h06C", 1000, 2)]
    [InlineData(GambleCounterKind.High, "h06D", 2000, 4)]
    [InlineData(GambleCounterKind.World, "H0AW", 3500, 5)]
    [InlineData(GambleCounterKind.Absalom, "h069", 500, 1)]
    public void TariffsArePinnedObjectMetadataNotObservedPayment(GambleCounterKind kind, string rawcode, int gold, int lumber)
    {
        var snapshot = Read(new GlobalsMemory())!;
        var tariff = Assert.Single(snapshot.Counters, counter => counter.Kind == kind).SourceTariff;
        Assert.NotNull(tariff);
        Assert.Equal(rawcode, tariff.UnitRawcode);
        Assert.Equal(gold, tariff.Gold);
        Assert.Equal(lumber, tariff.Lumber);
        Assert.False(tariff.IsPaymentReceipt);
        Assert.Equal("a9aa2cb9c08130c3bee970aecb05b62fdc3db867685f4997dd2533735d2ea278", tariff.UnitObjectSha256);
        Assert.Null(Assert.Single(snapshot.Counters, counter => counter.Kind == GambleCounterKind.LumberWisp).SourceTariff);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("map")]
    [InlineData("owner")]
    [InlineData("slot")]
    [InlineData("anchor")]
    [InlineData("address")]
    public void InvalidSourceGuardDoesNotReadMemory(string fault)
    {
        var reads = 0;
        byte[] Forbidden(ulong address, int size) { reads++; throw new InvalidOperationException(); }
        var result = new WarcraftGambleCountersReader().Read(Forbidden,
            fault == "version" ? "2.0.4.23746" : Version,
            fault == "map" ? "unbound" : MapHash,
            fault == "owner" ? null : fault == "slot" ? (byte)4 : (byte)1,
            fault == "anchor" ? null : fault == "address" ? 0UL : GlobalsMemory.Base);
        Assert.Null(result);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("type")]
    [InlineData("type-disagreement")]
    [InlineData("duplicate")]
    [InlineData("capacity")]
    [InlineData("short-read")]
    public void UnboundOrMalformedGlobalsAreNullNotZero(string fault)
    {
        var memory = new GlobalsMemory();
        var node = memory.Nodes["YE"];
        if (fault == "missing") memory.Put(node + 56, 0UL);
        if (fault == "short") memory.Array("YE", 9, 0);
        if (fault == "type") { memory.Put(node + 48, 13); memory.Put(node + 52, 13); }
        if (fault == "type-disagreement") memory.Put(node + 52, 13);
        if (fault == "duplicate") memory.Put(node + 40, memory.NameAddress("kE"));
        if (fault == "capacity") memory.Put(memory.Header("YE") + 24, 1);
        byte[] ReadMemory(ulong address, int size) => fault == "short-read" && address == memory.Data("YE")
            ? [] : memory.Read(address, size);
        Assert.Null(Read(memory, ReadMemory));
    }

    [Theory]
    [InlineData("IE", -1)]
    [InlineData("LE", -1)]
    [InlineData("ZE", 3)]
    [InlineData("VE", int.MaxValue)]
    [InlineData("DE", 9)]
    [InlineData("OE", 5)]
    [InlineData("QE", int.MaxValue)]
    [InlineData("nE", -1)]
    [InlineData("iE", 10)]
    [InlineData("vE", 7)]
    [InlineData("Nw", 12)]
    public void RejectsNegativeOverflowingOrIncoherentTotals(string name, int value)
    {
        var memory = new GlobalsMemory();
        memory.Put(memory.Data(name) + 4, value);
        Assert.Null(Read(memory));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("pointer")]
    [InlineData("name")]
    public void TwoCompleteSnapshotsMustAgreeEvenWhenEachCounterTupleIsValid(string fault)
    {
        var memory = new GlobalsMemory();
        var changed = false;
        byte[] ReadMemory(ulong address, int size)
        {
            var bytes = memory.Read(address, size);
            // Mutation after earlier arrays were individually checked, before the second complete read.
            if (!changed && address == memory.Data("Nw"))
            {
                changed = true;
                if (fault == "value")
                {
                    memory.Put(memory.Data("IE") + 4, 11);
                    memory.Put(memory.Data("LE") + 4, 4);
                }
                if (fault == "pointer") memory.Array("IE", 9, 0, 10);
                if (fault == "name") memory.Put(memory.Nodes["IE"] + 40, memory.NameAddress("LE"));
            }
            return bytes;
        }
        Assert.Null(Read(memory, ReadMemory));
        Assert.True(changed);
    }

    [Fact]
    public void CancellationBeforeOrDuringReadPropagatesAndNeverReturnsPreviousCounters()
    {
        var reader = new WarcraftGambleCountersReader();
        var memory = new GlobalsMemory();
        Assert.NotNull(Read(memory, reader: reader));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => reader.Read((_, _) => throw new InvalidOperationException(),
            Version, MapHash, 1, memory.Anchor, canceled.Token));
        using var during = new CancellationTokenSource();
        byte[] ReadMemory(ulong address, int size)
        {
            var bytes = memory.Read(address, size);
            during.Cancel();
            return bytes;
        }
        Assert.Throws<OperationCanceledException>(() => reader.Read(ReadMemory, Version, MapHash, 1, memory.Anchor, during.Token));
    }

    [Fact]
    public void ResetOrNewTableNeverReusesPreviousValuesAndBoundZeroIsValid()
    {
        var reader = new WarcraftGambleCountersReader();
        var old = new GlobalsMemory();
        Assert.NotNull(Read(old, reader: reader));
        Assert.Null(reader.Read(old.Read, Version, MapHash, 1, null));
        old.Put(old.Nodes["IE"] + 56, 0UL);
        Assert.Null(Read(old, reader: reader));
        var fresh = new GlobalsMemory();
        foreach (var name in fresh.Nodes.Keys) fresh.Array(name, 9, 0, 0);
        var snapshot = Read(fresh, reader: reader)!;
        Assert.All(snapshot.Counters, counter =>
        {
            Assert.Equal(0, counter.Attempts);
            Assert.Equal(0, counter.Successes);
            Assert.Equal(0, counter.Failures);
        });
    }

    [Fact]
    public void ProcessReadFailureReturnsNull()
    {
        var memory = new GlobalsMemory();
        Assert.Null(Read(memory, (_, _) => throw new Win32Exception(299)));
    }

    private static GambleCounterSnapshot? Read(GlobalsMemory memory,
        Func<ulong, int, byte[]>? read = null, WarcraftGambleCountersReader? reader = null) =>
        (reader ?? new()).Read(read ?? memory.Read, Version, MapHash, 1, memory.Anchor);

    private static void AssertTotals(GambleCounterSnapshot snapshot, GambleCounterKind kind, int attempts, int successes, int failures)
    {
        var counter = Assert.Single(snapshot.Counters, counter => counter.Kind == kind);
        Assert.Equal(attempts, counter.Attempts);
        Assert.Equal(successes, counter.Successes);
        Assert.Equal(failures, counter.Failures);
    }

    // Same named-global node/array layout exercised by BulletNativeGlobalsTests and RouteQuestMemory.
    // Synthesized bytes, not a claim of a live gambling-counter binding or native gameplay capture.
    private sealed class GlobalsMemory
    {
        internal const ulong Base = 0x100000;
        private readonly byte[] _bytes = new byte[0x20000];
        private int _next = 0x8000;
        internal Dictionary<string, ulong> Nodes { get; } = new(StringComparer.Ordinal);
        internal ulong Anchor => Nodes["IE"];
        internal int QuestReads { get; private set; }
        internal GlobalsMemory()
        {
            (string Name, int Value)[] values =
            [ ("IE", 10), ("LE", 3), ("ZE", 2), ("VE", 5), ("eE", 8), ("DE", 5),
              ("kE", 12), ("OE", 4), ("QE", 2), ("nE", 6), ("YE", 9), ("iE", 2),
              ("AE", 6), ("vE", 1), ("Gw", 11), ("Nw", 7) ];
            for (var index = 0; index < values.Length; index++)
            {
                var (name, value) = values[index];
                var node = Base + 0x100UL + (ulong)index * 72;
                Nodes.Add(name, node);
                Put(node + 32, index == values.Length - 1 ? 0UL : node + 72);
                var text = Base + 0x3000UL + (ulong)index * 32;
                Put(node + 40, text);
                Encoding.ASCII.GetBytes(name + "\0").CopyTo(_bytes, (int)(text - Base));
                Array(name, 9, 0, value);
            }
        }
        internal void Array(string name, int type, params int[] values)
        {
            var node = Nodes[name];
            var header = Base + (ulong)_next;
            _next += 128;
            Put(node + 48, type); Put(node + 52, type); Put(node + 56, header);
            Put(header + 8, values.Length); Put(header + 24, values.Length); Put(header + 16, header + 32);
            for (var index = 0; index < values.Length; index++) Put(header + 32 + (ulong)index * 4, values[index]);
        }
        internal ulong NameAddress(string name) => BitConverter.ToUInt64(Read(Nodes[name] + 40, 8));
        internal ulong Header(string name) => BitConverter.ToUInt64(Read(Nodes[name] + 56, 8));
        internal ulong Data(string name) => BitConverter.ToUInt64(Read(Header(name) + 16, 8));
        internal byte[] Read(ulong address, int size)
        {
            var bytes = address >= Base && address + (ulong)size <= Base + (ulong)_bytes.Length
                ? _bytes.AsSpan((int)(address - Base), size).ToArray() : [];
            if (size == 32 && (Encoding.ASCII.GetString(bytes).TrimEnd('\0') is "cr" or "gr")) QuestReads++;
            return bytes;
        }
        internal void Put(ulong address, int value) => BitConverter.GetBytes(value).CopyTo(_bytes, (int)(address - Base));
        internal void Put(ulong address, ulong value) => BitConverter.GetBytes(value).CopyTo(_bytes, (int)(address - Base));
    }
}
