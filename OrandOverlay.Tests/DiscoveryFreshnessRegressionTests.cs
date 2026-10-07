using System.Diagnostics;
using System.Numerics;
using System.Text;
using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class DiscoveryFreshnessRegressionTests
{
    private static readonly Lazy<DataCatalog> Bundled = new(() =>
    {
        var catalog = new DataCatalog();
        catalog.Load(mapVersion: "2.320");
        return catalog;
    });
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Context = new('A', 64);

    private sealed class Clock
    {
        public long Ticks = 1;
        public long Now() => Ticks;
        public void Advance(TimeSpan elapsed) =>
            Ticks = checked(Ticks + (long)(elapsed.TotalSeconds * Stopwatch.Frequency));
    }

    private static InventoryEntry Card() => new() { UnitId = "rawcode:I10h", Count = 1 };

    private static DiagnosticInventoryObservation Observe(TimeSpan duration, DateTimeOffset? started = null)
    {
        var began = started ?? Now;
        return DiagnosticInventoryObservation.Create(Bundled.Value, Warcraft300Diagnostic.Version,
            Warcraft300Diagnostic.Hash, Context, 2, 0, began, began + duration, duration,
            [Card()], [Card().UnitId], observedRound: 12);
    }

    [Fact]
    public void QualityAcceptedUnavailableObservationIsNotReady()
    {
        var overBudget = Observe(TimeSpan.FromSeconds(4));
        Assert.Equal(DiagnosticInventoryAvailability.Unavailable, overBudget.Availability);
        Assert.Contains("freshness", overBudget.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RecognitionState.TransientReadError,
            WarcraftMemoryRecognitionService.DiagnosticObservationState(true, overBudget));
        Assert.Equal(RecognitionState.TransientReadError,
            WarcraftMemoryRecognitionService.DiagnosticObservationState(false, overBudget));
        var fresh = Observe(TimeSpan.FromSeconds(0.2));
        Assert.Equal(DiagnosticInventoryAvailability.Ready, fresh.Availability);
        Assert.Equal(RecognitionState.Ready,
            WarcraftMemoryRecognitionService.DiagnosticObservationState(true, fresh));
        Assert.Equal(RecognitionState.TransientReadError,
            WarcraftMemoryRecognitionService.DiagnosticObservationState(false, fresh));
    }

    [Fact]
    public void FourSecondDiscoveryDoesNotPoisonFreshSampleRoundOrObservation()
    {
        var clock = new Clock();
        var memory = RoundMemory();
        var discoveryDone = 0L;
        memory.BeforeRead = (address, _) =>
        {
            if (address != Memory.ScanBase || discoveryDone != 0) return;
            clock.Advance(TimeSpan.FromSeconds(4));
            discoveryDone = clock.Now();
        };

        var growth = memory.Read(timestamp: clock.Now);
        Assert.True(discoveryDone > 1);
        Assert.True(growth.RoundInputs!.StartedTimestamp >= discoveryDone,
            "Round/sample timestamps must start after complete discovery, not at the 32s walk.");
        Assert.Equal(Memory.Unit, growth.UnitPointer);

        var round = Warcraft300ObservedRoundReader.Observation.Read(memory.Bytes, growth, Memory.Module,
            _ => ContextOf(memory), default, clock.Now);
        Assert.True(round.SuccessfulComparison);
        Assert.Equal(12, round.Round);

        var duration = Stopwatch.GetElapsedTime(growth.RoundInputs.StartedTimestamp, clock.Now());
        Assert.True(duration < DiagnosticInventoryObservation.FreshnessBudget);
        var observation = Observe(duration, Now);
        Assert.Equal(DiagnosticInventoryAvailability.Ready, observation.Availability);
        Assert.Equal(RecognitionState.Ready,
            WarcraftMemoryRecognitionService.DiagnosticObservationState(true, observation));
    }

    [Fact]
    public void SampleReadsPostDiscoveryValuesInsteadOfRestampingOpeningPayload()
    {
        var clock = new Clock();
        var memory = RoundMemory();
        var mutated = false;
        memory.BeforeRead = (address, _) =>
        {
            if (address != Memory.ScanBase || mutated) return;
            clock.Advance(TimeSpan.FromSeconds(4));
            memory.Put32(Memory.Unit + 0x178, 0x48303032);
            mutated = true;
        };

        var growth = memory.Read(timestamp: clock.Now);
        Assert.True(mutated);
        Assert.Equal(0x48303032U, growth.Rawcode);
        Assert.True(growth.RoundInputs!.StartedTimestamp > 1);
        Assert.True(Stopwatch.GetElapsedTime(1, growth.RoundInputs.StartedTimestamp) >= TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void OwnerUniquenessStillRunsOnEveryReadAfterSlowDiscovery()
    {
        var clock = new Clock();
        var memory = RoundMemory();
        var delayed = false;
        memory.BeforeRead = (address, _) =>
        {
            if (address != Memory.ScanBase || delayed) return;
            clock.Advance(TimeSpan.FromSeconds(4));
            delayed = true;
        };
        Assert.NotNull(memory.Read(timestamp: clock.Now).UnitPointer);
        Assert.Equal(1, memory.Enumerations);
        memory.Put64(Memory.ScanBase, Memory.Instance);
        memory.Put64(Memory.ScanBase + 8, Memory.Module + 0x13F3070);
        memory.Put64(Memory.ScanBase + 16, Memory.Table);
        var error = Assert.Throws<InvalidDataException>(() => memory.Read(timestamp: clock.Now));
        Assert.Contains("ambiguous", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, memory.Enumerations);
    }

    [Fact]
    public void CancellationStillAbortsSyntheticDiscovery()
    {
        var clock = new Clock();
        var memory = RoundMemory();
        using var cancel = new CancellationTokenSource();
        memory.BeforeRead = (address, _) =>
        {
            if (address != Memory.ScanBase) return;
            clock.Advance(TimeSpan.FromSeconds(4));
            cancel.Cancel();
        };
        Assert.Throws<OperationCanceledException>(() => memory.Read(cancel.Token, timestamp: clock.Now));
    }

    [Fact]
    public void DiscoveryTimeCapRemainsThirtyTwoSeconds()
    {
        var clock = new Clock();
        var memory = RoundMemory();
        memory.BeforeRead = (address, _) =>
        {
            if (address == Memory.ScanBase) clock.Advance(TimeSpan.FromSeconds(32));
        };
        var error = Assert.Throws<InvalidDataException>(() => memory.Read(timestamp: clock.Now));
        Assert.Contains("time cap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoveryContinuesWithoutReadingThirdBasicUntilDeliveryCapacityRecovers()
    {
        var elapsed = TimeSpan.Zero;
        var memory = RoundMemory();
        var firstConsumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterBoundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseDiscoveryRead = new ManualResetEventSlim();
        using var stop = new CancellationTokenSource();
        var factoryRuns = 0;
        var activeReads = 0;
        var maximumReads = 0;

        byte[] Read(ulong address, int count)
        {
            var active = Interlocked.Increment(ref activeReads);
            maximumReads = Math.Max(maximumReads, active);
            try
            {
                if (address == Memory.ScanBase)
                {
                    laterBoundary.TrySetResult();
                    releaseDiscoveryRead.Wait(stop.Token);
                }
                return memory.Bytes(address, count);
            }
            finally { Interlocked.Decrement(ref activeReads); }
        }

        void Produce(Action<string> emit, Func<Func<string>, bool> tryEmit, CancellationToken token)
        {
            emit("B0");
            firstConsumed.Task.Wait(token);
            emit("B1");
            var checkpoint = new DiagnosticDiscoveryCheckpoint(() =>
            {
                thirdAttempted.TrySetResult();
                return tryEmit(() =>
                {
                    Interlocked.Increment(ref factoryRuns);
                    var rawcode = BitConverter.ToUInt32(Read(Memory.Unit + 0x178, 4));
                    elapsed += TimeSpan.FromMilliseconds(40);
                    return $"B2:{rawcode:X8}";
                });
            }, () => elapsed);
            elapsed = TimeSpan.FromMilliseconds(250);
            _ = memory.Reader.Read(Read, memory.Regions, Memory.Module, memory.View, Memory.World,
                memory.Session, memory.Expected, token, discoveryCheckpoint: checkpoint.Poll,
                timestamp: () => 1);
            emit("FINAL");
        }

        var stream = DiagnosticRecognitionStream.Run<string>(Produce, stop.Token);
        var iterator = stream.GetAsyncEnumerator();
        try
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("B0", iterator.Current);
            firstConsumed.TrySetResult();
            await thirdAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var reachedLaterBoundary = true;
            try { await laterBoundary.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { reachedLaterBoundary = false; }
            Assert.True(reachedLaterBoundary,
                "Discovery must pass the due checkpoint while B1 still occupies delivery capacity.");

            Assert.Equal(0, Volatile.Read(ref factoryRuns));
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("B1", iterator.Current);
            memory.Put32(Memory.Unit + 0x178, 0x48303032);
            releaseDiscoveryRead.Set();
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("B2:48303032", iterator.Current);
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal("FINAL", iterator.Current);
            Assert.False(await iterator.MoveNextAsync());
            Assert.Equal(1, Volatile.Read(ref factoryRuns));
            Assert.Equal(1, maximumReads);
        }
        finally
        {
            stop.Cancel();
            releaseDiscoveryRead.Set();
            await iterator.DisposeAsync();
        }
    }

    private static Warcraft300ObservedRoundReader.Context ContextOf(Memory memory) =>
        new(memory.Session, Memory.Module, memory.View, Memory.World);

    private const ulong Pb = 0xB00000, Eb = 0xB10000, Qg = 0xB20000, PbName = 0xB30000, EbName = 0xB40000, QgName = 0xB50000,
        Dialog = 0xC00000, DialogUi = 0xC10000, Timer = 0xC20000, Frame = 0xC30000, DialogName = 0xD00000, FrameName = 0xD10000, Title = 0xD20000,
        DialogRecord = 0xE00000, TimerRecord = 0xE10000;

    private static Memory RoundMemory(int round = 12)
    {
        var memory = new Memory();
        foreach (var address in new[] { Pb, Eb, Qg, PbName, EbName, QgName, Dialog, DialogUi, Timer, Frame, DialogName, FrameName, Title, DialogRecord, TimerRecord })
            memory.Allocate(address, 0x3000);
        var nodes = new[] { (Pb, PbName, "pb", 4U), (Eb, EbName, "Eb", 4U), (Qg, QgName, "qg", 7U) };
        memory.Put64(Memory.Extra + 32, Pb); memory.Put64(Memory.Table + 16, Qg + 24);
        for (var i = 0; i < 3; i++)
        {
            var node = nodes[i];
            memory.Put64(node.Item1 + 24, i == 0 ? Memory.Extra + 24 : nodes[i - 1].Item1 + 24);
            memory.Put64(node.Item1 + 32, i == 2 ? Memory.Table + 17 : nodes[i + 1].Item1);
            memory.Put64(node.Item1 + 40, node.Item2);
            memory.Put32(node.Item1 + 48, node.Item4); memory.Put32(node.Item1 + 52, node.Item4);
            memory.Name(node.Item2, node.Item3);
            memory.Expected[node.Item3] = (int)node.Item4;
        }
        memory.Put32(Pb + 56, (uint)round); memory.Put32(Eb + 56, (uint)round + 1); memory.Put32(Qg + 56, 0x100001);
        memory.Put32(Memory.Manager + 0x290, 2); memory.Put32(Memory.JassTable + 24, 1); memory.Put64(Memory.JassTable + 32, Dialog);
        memory.Put64(Dialog, Memory.Module + 0x2730B08); memory.Put64(Dialog + 0x18, 0x600000001);
        memory.Put64(Dialog + 0x58, DialogUi); memory.Put64(Dialog + 0x60, 0x700000002);
        memory.Put64(DialogUi, Memory.Module + 0x2768190); memory.Put64(DialogUi + 0x40, Memory.Ui);
        memory.Put64(DialogUi + 0x280, DialogName); memory.Put64(DialogUi + 0x2D8, Timer); memory.Put64(DialogUi + 0x298, Frame);
        memory.Name(DialogName, "TimerDialog");
        memory.Put64(Timer, Memory.Module + 0x26E04E0); memory.Put64(Timer + 0x18, 0x700000002);
        memory.Put64(Frame, Memory.Module + 0x228F5D0); memory.Put64(Frame + 0x40, DialogUi);
        memory.Put64(Frame + 0x280, FrameName); memory.Name(FrameName, "TimerDialogTitle");
        memory.Put64(Frame + 0x4C8, Title); memory.Put64(Frame + 0x4D0, Title);
        memory.Put(Title, new byte[96]);
        memory.Put(Title, Encoding.UTF8.GetBytes($"|cffFF0000현재 라운드|r : {round}|r\0"));
        memory.Put32(Memory.Registry + 0x30, 3);
        foreach (var native in new[] { (1UL, 6U, DialogRecord, Dialog), (2UL, 7U, TimerRecord, Timer) })
        {
            memory.Put32(Memory.NativeTable + native.Item1 * 16, 0xFFFFFFFE);
            memory.Put64(Memory.NativeTable + native.Item1 * 16 + 8, native.Item3);
            memory.Put32(native.Item3 + 0x18, 0x2B61676C);
            memory.Put32(native.Item3 + 0x24, native.Item2);
            memory.Put64(native.Item3 + 0x90, native.Item4);
        }
        return memory;
    }

    private sealed class Memory
    {
        internal const ulong Module = 0x140000000, Root = 0x200000, Player = 0x300000,
            World = 0x310000, Ui = 0x320000, Instance = 0x400000, Script = 0x410000,
            Manager = 0x420000, Registry = 0x430000, ScanBase = 0x600000, Aggregate = ScanBase + 0xFFF8,
            Table = 0x700000, Qr = 0x710000, Extra = 0x720000, QrName = 0x730000, ExtraName = 0x740000,
            Array = 0x750000, ArrayData = 0x760000, JassTable = 0x800000, Unit = 0x900000,
            NativeTable = 0xA00000, Record = 0xA10000;
        private readonly List<(ulong Address, byte[] Bytes)> blocks = new();
        internal readonly Warcraft300GrowthReader Reader = new();
        internal readonly Dictionary<string, int> Expected = new(StringComparer.Ordinal) { ["QR"] = 12 };
        internal readonly List<(ulong Address, int Length)> Calls = new();
        internal Warcraft300Diagnostic.View View = new(Root, 0, Player);
        internal string Session = "session";
        internal int Enumerations;
        internal Action<ulong, int>? BeforeRead;
        internal Memory()
        {
            foreach (var address in new[] { Root, Player, World, Ui, Instance, Script, Manager, Registry,
                Table, Qr, Extra, QrName, ExtraName, Array, ArrayData, JassTable, Unit, NativeTable, Record })
                Allocate(address, 0x3000);
            Allocate(ScanBase, 0x10030);
            foreach (var rva in new ulong[] { 0x2E9AD00, 0x2F5EF00, 0x2F85360, 0x2F807F0 }) Allocate(Module + rva, 8);
            ulong encoded;
            unchecked { encoded = BitOperations.RotateRight(((Root - 0x2D2C27903E7F5D3DUL) ^ 0x3A11C7B7EF67132BUL) - 0x5BE06F37FC9B5B29UL, 29); }
            Put64(Module + 0x2E9AD00, encoded);
            Put64(Root, Module + 0x26C8C70); Put32(Root + 0x2698, 28); SetView(0);
            Put64(Player, Module + 0x26C87D8); Put64(World, Module + 0x2764A20); Put64(World + 0x40, Ui);
            Put64(Ui, Module + 0x275ED08); Put64(Module + 0x2F5EF00, Ui); Put64(Module + 0x2F85360, Ui);
            Put64(Root + 0x25D0, Instance); Put64(Root + 0x25E0, Script); Put64(Root + 0x2620, Manager);
            Put64(Instance, Module + 0x27ECBC0); Put64(Script, Module + 0x27ECC40);
            Put64(Module + 0x2F807F0, Registry);
            Put64(Aggregate, Instance); Put64(Aggregate + 8, Module + 0x13F3070); Put64(Aggregate + 16, Table);
            Put64(Table, Module + 0x2809B78); Put32(Table + 8, 24); Put64(Table + 24, Qr); Put64(Table + 16, Extra + 24);
            Put64(Qr + 24, Table + 16); Put64(Qr + 32, Extra); Put64(Qr + 40, QrName);
            Put32(Qr + 48, 12); Put32(Qr + 52, 12); Put64(Qr + 56, Array); Name(QrName, "QR");
            Put64(Extra + 24, Qr + 24); Put64(Extra + 32, Table + 17); Put64(Extra + 40, ExtraName);
            Put32(Extra + 48, 0); Put32(Extra + 52, 7); Name(ExtraName, "extra");
            Put64(Array, Module + 0x2809EF8); Put32(Array + 8, 1); Put64(Array + 16, ArrayData); Put32(Array + 24, 4);
            Put32(ArrayData, 0x100000); Put32(Manager + 0x290, 1); Put64(Manager + 0x298, JassTable);
            Put32(JassTable, 1); Put64(JassTable + 8, Unit);
            Put64(Unit, Module + 0x2792E78); Put32(Unit + 0x18, 0); Put32(Unit + 0x1C, 5);
            Put32(Unit + 0x1C0, 27); Put32(Unit + 0x178, 0x48303031);
            Put32(Registry + 0x30, 1); Put64(Registry + 0x18, NativeTable);
            Put32(NativeTable, 0xFFFFFFFE); Put64(NativeTable + 8, Record);
            Put32(Record + 0x24, 5); Put32(Record + 0x18, 0x2B61676C); Put64(Record + 0x90, Unit);
        }
        internal void SetView(ushort slot)
        {
            Put(Root + 0x262C, BitConverter.GetBytes(slot)); Put64(Root + 0x26A0 + slot * 8UL, Player);
            View = new(Root, slot, Player);
        }
        internal void Allocate(ulong address, int length) => blocks.Add((address, new byte[length]));
        internal void Put(ulong address, byte[] value)
        {
            var block = blocks.Single(b => address >= b.Address && address - b.Address + (ulong)value.Length <= (ulong)b.Bytes.Length);
            value.CopyTo(block.Bytes, (int)(address - block.Address));
        }
        internal void Put64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal void Put32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void Name(ulong address, string value) { Put(address, new byte[512]); Put(address, Encoding.ASCII.GetBytes(value + "\0")); }
        internal byte[] Bytes(ulong address, int length)
        {
            BeforeRead?.Invoke(address, length); Calls.Add((address, length));
            var found = blocks.Where(b => address >= b.Address && address - b.Address + (ulong)length <= (ulong)b.Bytes.Length).ToArray();
            if (found.Length != 1) throw new InvalidDataException("Unmapped synthetic read");
            return found[0].Bytes.AsSpan((int)(address - found[0].Address), length).ToArray();
        }
        internal IEnumerable<MemoryRegion> Regions()
        {
            Enumerations++;
            return [new MemoryRegion(ScanBase, 0x10030)];
        }
        internal Warcraft300GrowthObservation Read(CancellationToken token = default, Func<long>? timestamp = null) =>
            Reader.Read(Bytes, Regions, Module, View, World, Session, Expected, token, timestamp: timestamp);
    }
}
