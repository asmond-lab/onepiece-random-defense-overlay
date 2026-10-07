using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftCombatReaderTests
{
    [Fact]
    public void LaneComesFromMarkerAndSharedBossDoesNotBecomeLocalLane()
    {
        var bytes = new Dictionary<ulong, byte>();
        void Put(ulong address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i];
        }
        void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        byte[] Read(ulong address, int length) => Enumerable.Range(0, length)
            .Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
        const ulong unit = 0x100000, module = 0x400000;
        U64(unit, 0x401000);
        U64(0x401178, module + 0x1163ad0);
        U64(0x401278, module + 0x1163a00);
        U64(0x401280, module + 0x1163a20);
        U32(unit + 0x178, 0x6f303243); Put(unit + 0x1c0, [6]);
        Put(unit + 0x2e8, BitConverter.GetBytes(-13.5f));
        U64(unit + 0x258, ulong.MaxValue); U64(unit + 0x3b8, ulong.MaxValue);
        U64(unit + 0x558, 9UL << 32);
        U64(module + 0x2b808c0, 0x120000);
        U32(0x120030, 3); U64(0x120018, 0x130000);
        uint[] codes = [0x41393031, 0x41393032, 0x4130344d];
        for (var i = 0; i < codes.Length; i++)
        {
            var entry = 0x140000UL + (ulong)i * 0x1000;
            var ability = 0x180000UL + (ulong)i * 0x1000;
            U32(0x130000UL + (ulong)i * 16, 0xfffffffe);
            U64(0x130008UL + (ulong)i * 16, entry);
            U32(entry + 0x24, 9); U64(entry + 0x30, 0); U64(entry + 0x90, ability);
            U32(ability + 0x70, codes[i]); U32(ability + 0x9c, i == 2 ? 75U : 0U);
            U64(ability + 0x58, i == 2 ? ulong.MaxValue : (9UL << 32) | (uint)(i + 1));
        }
        var reader = new WarcraftCombatReader(Read, module, 0x4000000);
        var state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x6f303243, 6, 0, 1);
        Assert.NotNull(state);
        Assert.Equal(CombatUnitKind.LaneBoss, state.Kind);
        Assert.Equal((byte)0, state.LaneSlot);
        Assert.Equal(75, state.ArmorBreakStacks);
        Assert.Equal(-13.5f, state.NativeArmor);
        Assert.Null(state.Life);
        Assert.Null(state.Position);
        U32(0x18009c, 1);
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x6f303243, 6, 0, 1));
        Put(unit + 0x1c0, [5]);
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x6f303243, 5, 0, 1);
        Assert.NotNull(state);
        Assert.Equal(CombatUnitKind.SharedBoss, state.Kind);
        Assert.Null(state.LaneSlot);
        Put(unit + 0x2e8, BitConverter.GetBytes(float.NaN));
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x6f303243, 5, 0, 1);
        Assert.NotNull(state);
        Assert.Null(state.NativeArmor);
        U32(0x142024, 10);
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x6f303243, 5, 0, 1));
    }
}
