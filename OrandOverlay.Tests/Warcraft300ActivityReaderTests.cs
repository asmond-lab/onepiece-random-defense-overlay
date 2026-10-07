using System.Diagnostics;
using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class Warcraft300ActivityReaderTests
{
    [Theory]
    [InlineData("2.321", "NU")]
    [InlineData("2.322", "xT")]
    public void ReadsFreshCurrentViewIntegerCounterWithoutGameplayAuthority(string version, string counter)
    {
        var memory = new Memory(version: version);
        var result = memory.Read();
        Assert.Equal("ready", result.Status);
        Assert.NotNull(result.Values);
        Assert.Equal(7, result.Values[counter]);
        Assert.InRange(result.ReadBytes, 1, 32768);
        Assert.InRange(result.ReadCalls, 1, 1024);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("pointer")]
    [InlineData("metadata")]
    [InlineData("context")]
    [InlineData("vtable")]
    [InlineData("bounds")]
    public void ChangedOrInvalidEvidenceNeverBecomesAZeroCounter(string mutation)
    {
        var memory = new Memory();
        var counterReads = 0;
        switch (mutation)
        {
            case "value":
                memory.BeforeRead = address =>
                {
                    if (address == Memory.Data && ++counterReads == 2) memory.D(Memory.Data, 8);
                };
                break;
            case "pointer": memory.Q(Memory.Counter + 56, 0); break;
            case "metadata": memory.D(Memory.Counter + 48, 12); break;
            case "context": memory.CurrentWorld++; break;
            case "vtable": memory.Q(Memory.Array, 1); break;
            case "bounds": memory.D(Memory.Array + 8, 0); break;
        }
        Assert.Null(memory.Read().Values);
    }

    [Fact]
    public void UninitializedArrayDoesNotHideASeparateObservedCounterOrBecomeZero()
    {
        var result = new Memory(includeUninitialized: true).Read();
        Assert.NotNull(result.Values);
        Assert.Equal(7, result.Values["NU"]);
        Assert.False(result.Values.ContainsKey("f"));
        Assert.Equal("partial", result.Status);
        Assert.Collection(result.UnavailableNames, name => Assert.Equal("f", name));
    }

    [Fact]
    public void CancellationAndExpiredInputDoNotReadMemory()
    {
        var memory = new Memory();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => memory.Read(stop.Token));
        Assert.Empty(memory.Calls);
        Assert.Null(memory.Read(timestamp: () => memory.Start + 4 * Stopwatch.Frequency).Values);
        Assert.Empty(memory.Calls);
    }

    private sealed class Memory
    {
        internal const ulong Module = 0x140000000, Root = 0x100000, Player = 0x110000,
            World = 0x120000, Ui = 0x130000, Instance = 0x140000, Script = 0x150000,
            Manager = 0x160000, Registry = 0x170000, Owner = 0x180000, Table = 0x190000,
            Counter = 0x200000, Array = 0x210000, Data = 0x220000;
        private readonly Dictionary<ulong, byte> _bytes = [];
        internal readonly long Start = Stopwatch.GetTimestamp();
        internal readonly List<ulong> Calls = [];
        internal Action<ulong>? BeforeRead;
        internal ulong CurrentWorld = World;
        private readonly Warcraft300GrowthObservation _growth;

        internal Memory(bool includeUninitialized = false, string version = "2.321")
        {
            Q(Module + 0x2F5EF00, Ui); Q(Module + 0x2F85360, Ui);
            Q(Ui, Module + 0x275ED08); Q(World, Module + 0x2764A20); Q(World + 0x40, Ui);
            Q(Root + 0x25D0, Instance); Q(Root + 0x25E0, Script); Q(Root + 0x2620, Manager);
            Q(Module + 0x2F807F0, Registry);
            var round = new[]
            {
                Node(0x300000, version == "2.322" ? "GR" : "Ag", 4),
                Node(0x310000, version == "2.322" ? "hR" : "lg", 4),
                Node(0x320000, version == "2.322" ? "Vs" : "yp", 7)
            };
            var counter = Node(Counter, version == "2.322" ? "xT" : "NU", 9);
            var selected = new List<Warcraft300SnapshotNode> { new(counter.Name, counter.Address, counter.Metadata) };
            if (includeUninitialized)
            {
                var empty = Node(0x240000, "f", 9);
                selected.Add(new(empty.Name, empty.Address, empty.Metadata));
            }
            Q(Counter + 56, Array);
            Q(Array, Module + 0x2809EF8); D(Array + 8, 4); Q(Array + 16, Data); D(Array + 24, 4); D(Data, 7);
            var context = Warcraft300RoundInputs.TryCreate(Start, "fixture", Module, new(Root, 0, Player),
                World, Ui, Instance, Script, Manager, Registry, Owner, Table,
                Bytes(Owner, 24), Bytes(Table, 72), Bytes(Instance, 48), Bytes(Script, 16),
                round, Map2320GrowthSource.LoadBundled(version))!;
            _growth = new(null, null, null, context.View, "fixture", World, Instance, Script,
                Manager, Registry, "fixture", Owner, Table, 0, 0)
            {
                RoundInputs = context, SampleStartedTimestamp = Start,
                ActivityInputs = new(context, selected.AsReadOnly())
            };
            Calls.Clear();
        }

        private (string Name, ulong Address, byte[] Metadata) Node(ulong address, string name, uint tag)
        {
            Q(address + 40, address + 128); D(address + 48, tag); D(address + 52, tag);
            Put(address + 128, Encoding.ASCII.GetBytes(name + "\0"));
            return (name, address, Bytes(address + 24, 32));
        }
        internal void Q(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal void D(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        private void Put(ulong address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) _bytes[address + (ulong)i] = value[i];
        }
        private byte[] Bytes(ulong address, int count)
        {
            BeforeRead?.Invoke(address); Calls.Add(address);
            return Enumerable.Range(0, count).Select(i => _bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
        }
        internal Warcraft300ActivityObservation Read(CancellationToken token = default, Func<long>? timestamp = null) =>
            Warcraft300ActivityReader.Read(Bytes, _growth,
                _ => new("fixture", Module, _growth.CurrentView, CurrentWorld),
                token, timestamp ?? (() => Start));
    }
}
