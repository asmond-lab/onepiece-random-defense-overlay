using Xunit;
namespace OrandOverlay.Tests;

public sealed class BlackMariaReaderTests
{
    [Theory]
    [InlineData("A09R", "Stun")]
    [InlineData("A0T7", "Slow")]
    [InlineData("A0T6", "Burn")]
    public void NativeSinglePositiveModeIsObserved(string ability, string expected)
    {
        var state = new Bytes(ability).Observe();
        Assert.NotNull(state);
        Assert.Equal(expected, state.BlackMariaSelectedMode?.ToString());
    }

    [Theory]
    [InlineData("foreign")][InlineData("dead")][InlineData("lifeUnknown")][InlineData("legend")]
    [InlineData("missing")][InlineData("multiple")][InlineData("duplicateAbility")][InlineData("zero")]
    [InlineData("negative")][InlineData("version")][InlineData("map")][InlineData("generation")][InlineData("unstable")]
    public void UntrustedNativeSelectionIsNeverKnown(string blocked)
    {
        var bytes = blocked switch { "missing" => new Bytes("A0T5"), "multiple" => new Bytes("A09R", "A0T7"),
            "duplicateAbility" => new Bytes("A09R", "A09R"), _ => new Bytes("A09R") };
        if (blocked == "dead") bytes.Put(0x2000d0, BitConverter.GetBytes(0f));
        if (blocked == "lifeUnknown") bytes.U64(Bytes.Unit + 0x258, ulong.MaxValue);
        if (blocked == "zero") bytes.U32(0x18009c, uint.MaxValue);
        if (blocked == "negative") bytes.U32(0x18009c, uint.MaxValue - 1);
        if (blocked == "generation") bytes.U32(0x140024, 10);
        var reads = 0;
        var state = bytes.Observe(owner: blocked == "foreign" ? (byte)1 : (byte)0,
            rawcode: blocked == "legend" ? "h02X" : "h04U", version: blocked == "version" ? "unknown" : "2.0.4.23745",
            hash: blocked == "map" ? "unknown" : null,
            mutate: (address, _, value) => blocked == "unstable" && address == 0x180070 && ++reads > 1
                ? BitConverter.GetBytes(Bytes.Code("A0T7")) : value);
        Assert.Null(state?.BlackMariaSelectedMode);
    }

    internal sealed class Bytes
    {
        private readonly Dictionary<ulong, byte> _bytes = new();
        internal const ulong Unit = 0x100000, Module = 0x400000;
        internal Bytes(params string[] abilities)
        {
            U64(Unit, 0x401000);
            U64(0x401178, Module + 0x1163ad0); U64(0x401278, Module + 0x1163a00); U64(0x401280, Module + 0x1163a20);
            U32(Unit + 0x178, Code("h04U")); Put(Unit + 0x1c0, [0]);
            U64(Unit + 0x258, (9UL << 32) | 10); U64(Unit + 0x3b8, ulong.MaxValue);
            U64(Module + 0x2b808c0, 0x120000); U32(0x120030, 11); U64(0x120018, 0x130000);
            U64(Unit + 0x558, abilities.Length == 0 ? ulong.MaxValue : 9UL << 32);
            for (var i = 0; i < abilities.Length; i++)
            {
                var entry = 0x140000UL + (ulong)i * 0x1000;
                var ability = 0x180000UL + (ulong)i * 0x1000;
                Entry(i, entry); U64(entry + 0x90, ability);
                U32(ability + 0x70, Code(abilities[i])); U32(ability + 0x9c, 0);
                U64(ability + 0x58, i == abilities.Length - 1 ? ulong.MaxValue : (9UL << 32) | (uint)(i + 1));
            }
            Entry(10, 0x200000);
            Put(0x2000d0, BitConverter.GetBytes(100f)); Put(0x2000e0, BitConverter.GetBytes(100f));
            U64(Module + 0x2b80848, 0x220000); Put(0x220090, BitConverter.GetBytes(1f));
        }
        private void Entry(int index, ulong entry)
        {
            U32(0x130000UL + (ulong)index * 16, 0xfffffffe); U64(0x130008UL + (ulong)index * 16, entry);
            U32(entry + 0x24, 9); U64(entry + 0x30, 0);
        }
        internal void Put(ulong address, byte[] bytes) { for (var i = 0; i < bytes.Length; i++) _bytes[address + (ulong)i] = bytes[i]; }
        internal void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        internal void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        internal static uint Code(string value) => value.Aggregate(0U, (code, c) => (code << 8) | c);
        internal CombatUnitState? Observe(byte owner = 0, string rawcode = "h04U", string version = "2.0.4.23745", string? hash = null,
            Func<ulong, int, byte[], byte[]>? mutate = null)
        {
            Put(Unit + 0x1c0, [owner]); U32(Unit + 0x178, Code(rawcode));
            byte[] Read(ulong address, int count) {
                var bytes = Enumerable.Range(0, count).Select(i => _bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
                return mutate?.Invoke(address, count, bytes) ?? bytes;
            }
            return new WarcraftCombatReader(Read, Module, 0x4000000).Read(version, hash ?? RouteQuestCatalog.MapScriptSha256,
                Unit, Code(rawcode), owner, 0, 1);
        }
    }
}
