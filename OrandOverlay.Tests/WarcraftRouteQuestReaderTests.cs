using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftRouteQuestReaderTests
{
    [Fact]
    public void ReadsRandomAssignedSlotsAndCompletionWithoutTreatingDefaultsAsAssigned()
    {
        var memory = new QuestMemory();
        memory.Assign(8, 0, 0, true);
        memory.Assign(16, 0, 1, true);
        memory.Assign(11, 0, 2, false);
        var result = memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0);
        Assert.True(result.IsVerified, result.Detail);
        Assert.Equal(new[] { "Q008", "Q016", "Q011" }, result.Assigned.Select(x => x.QuestId));
        Assert.Equal(new[] { true, true, false }, result.Assigned.Select(x => x.Completed));
    }

    [Fact]
    public void ForeignPlayerAssignedZeroSlotDoesNotBecomeLocalCompletedQuest()
    {
        var memory = new QuestMemory();
        memory.Assign(8, 0, 0, true);
        memory.Assign(16, 0, 1, true);
        memory.Assign(11, 0, 2, false);
        memory.Assign(0, 2, 0, false);
        memory.Assign(1, 2, 1, false);
        memory.Assign(2, 2, 2, false);
        var result = memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 2);
        Assert.True(result.IsVerified, result.Detail);
        Assert.Equal(new[] { "Q000", "Q001", "Q002" }, result.Assigned.Select(x => x.QuestId));
    }

    [Fact]
    public void UnverifiedOwnerOrWrongVersionCannotProduceQuestFacts()
    {
        var memory = new QuestMemory();
        var reader = memory.Reader();
        Assert.False(reader.Read("different", RouteQuestCatalog.MapScriptSha256, 0).IsVerified);
        Assert.False(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, null).IsVerified);
        Assert.False(reader.Read("2.0.4.23745", "wrong-map", 0).IsVerified);
    }

    [Fact]
    public void DuplicateSlotsAndCorruptPoolRemainUnknown()
    {
        var memory = new QuestMemory();
        memory.Assign(8, 0, 0, true);
        memory.Assign(16, 0, 0, true);
        memory.Assign(11, 0, 2, false);
        Assert.False(memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0).IsVerified);
        memory = new QuestMemory();
        memory.Assign(8, 0, 0, true);
        memory.Assign(16, 0, 1, true);
        memory.Assign(11, 0, 2, false);
        memory.CorruptPool();
        Assert.False(memory.Reader().Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0).IsVerified);
    }

    [Fact]
    public void CachedReaderObservesCompletionAndInvalidatesBrokenNodesWithoutRescanStorm()
    {
        var memory = new QuestMemory();
        memory.Assign(8, 0, 0, false);
        memory.Assign(16, 0, 1, false);
        memory.Assign(11, 0, 2, false);
        var reader = memory.Reader();
        Assert.True(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0).IsVerified);
        memory.Complete(8);
        Assert.True(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0).Assigned[0].Completed);
        Assert.Equal(2, memory.ScanPasses);
        memory.BreakNode();
        Assert.False(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0).IsVerified);
        Assert.False(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, 0).IsVerified);
        Assert.Equal(2, memory.ScanPasses);
    }

    [Fact]
    public void CancelledDiscoveryPropagatesCancellation()
    {
        var memory = new QuestMemory();
        Assert.Throws<OperationCanceledException>(() => memory.Reader().Read("2.0.4.23745",
            RouteQuestCatalog.MapScriptSha256, 0, new CancellationToken(true)));
    }

    private sealed class QuestMemory
    {
        private const ulong Base = 0x100000;
        private readonly byte[] _bytes = new byte[0x20000];
        private readonly Dictionary<string, int> _nodes = new(StringComparer.Ordinal);
        private int _next = 0x8000;
        private int _assigned;
        private readonly HashSet<int> _assignedIds = [];
        internal int ScanPasses { get; private set; }
        private static readonly string[] Aliases = "Dy Oy Qy Yy iy vy Gr Er wr Mr Sr Xr cr Br ar dr gr yr rr mr sr Hr hr tr pr Wr Cr fr xr Kr Ir Vr Zr Dr".Split(' ');

        internal QuestMemory()
        {
            var names = Aliases.Concat(new[] { "SE", "Sh", "Mh" }).ToArray();
            for (var i = 0; i < names.Length; i++)
            {
                var offset = 0x100 + i * 72;
                _nodes[names[i]] = offset;
                Put64(offset + 24, i == 0 ? 0 : Base + (ulong)(offset - 72 + 24));
                Put64(offset + 32, i + 1 == names.Length ? 0 : Base + (ulong)(offset + 72));
                Put64(offset + 40, Base + (ulong)(0x3000 + i * 32));
                Encoding.UTF8.GetBytes(names[i] + "\0").CopyTo(_bytes, 0x3000 + i * 32);
                var type = names[i] == "Sh" ? 4 : names[i] == "SE" || i < Aliases.Length && i % 2 == 0 ? 13 : 9;
                Put32(offset + 48, type);
                Put32(offset + 52, type);
            }
            Put32(_nodes["Sh"] + 56, 17);
            Array("SE", new[] { 1, 0, 1, 0 });
            Array("Mh", Enumerable.Range(0, 17).Select(i => Rawcode($"Q{i:000}")).ToArray());
        }

        internal WarcraftRouteQuestReader Reader() => new(Read, () =>
        {
            ScanPasses++;
            return new[] { (Base, _bytes) };
        });
        internal void BreakNode() => Put32(_nodes["gr"] + 48, 4);
        internal void Complete(int quest) => SetArrayValue(Aliases[quest * 2], 0);
        internal void CorruptPool() => SetArrayValue("Mh", Rawcode("Q008"));
        private void SetArrayValue(string name, int value)
        {
            var header = (int)(BitConverter.ToUInt64(_bytes, _nodes[name] + 56) - Base);
            var data = (int)(BitConverter.ToUInt64(_bytes, header + 16) - Base);
            Put32(data, value);
        }

        internal void Assign(int quest, int owner, int slot, bool completed)
        {
            var active = new int[owner + 1];
            active[owner] = completed ? 0 : 1;
            var slots = new int[owner + 1];
            slots[owner] = slot;
            Array(Aliases[quest * 2], active);
            Array(Aliases[quest * 2 + 1], slots);
            _assigned++;
            _assignedIds.Add(quest);
            Put32(_nodes["Sh"] + 56, 17 - _assigned);
            Array("Mh", Enumerable.Range(0, 17).OrderBy(i => _assignedIds.Contains(i)).Select(i => Rawcode($"Q{i:000}")).ToArray());
        }

        private void Array(string name, int[] values)
        {
            var header = _next;
            _next += 128;
            Put64(_nodes[name] + 56, Base + (ulong)header);
            Put64(header, 0x7ff00000);
            Put32(header + 8, values.Length);
            Put32(header + 24, name == "Mh" ? 22 : values.Length);
            Put64(header + 16, Base + (ulong)(header + 32));
            for (var i = 0; i < values.Length; i++) Put32(header + 32 + i * 4, values[i]);
        }

        private byte[] Read(ulong address, int length) => address >= Base && address + (ulong)length <= Base + (ulong)_bytes.Length
            ? _bytes.AsSpan((int)(address - Base), length).ToArray() : [];
        private void Put32(int offset, int value) => BitConverter.GetBytes(value).CopyTo(_bytes, offset);
        private void Put64(int offset, ulong value) => BitConverter.GetBytes(value).CopyTo(_bytes, offset);
        private static int Rawcode(string code) => code.Aggregate(0, (value, c) => (value << 8) | c);
    }
}
