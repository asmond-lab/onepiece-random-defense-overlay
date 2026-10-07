using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftOrderObservationTests
{
    [Fact]
    public void LiveAgentGuardProtectsOrderAndBaseAttackCooldown()
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
        const ulong module = 0x400000, unit = 0x100000, self = 7UL << 32;
        U64(unit, 0x401000);
        U64(0x401178, module + 0x1163ad0); U64(0x401278, module + 0x1163a00);
        U64(0x401280, module + 0x1163a20);
        U32(unit + 0x178, 0x68303831); Put(unit + 0x1c0, [0]);
        U64(unit + 0x258, ulong.MaxValue); U64(unit + 0x3b8, ulong.MaxValue);
        U64(unit + 0x558, ulong.MaxValue); U64(unit + 0x18, self);
        U64(unit + 0x500, self | 1);
        U64(unit + 0x5c0, 0x190000); Put(0x190200, BitConverter.GetBytes(1.2f));
        U64(module + 0x2b808c0, 0x120000);
        U32(0x120030, 2); U64(0x120018, 0x130000);
        for (var i = 0; i < 2; i++)
        {
            var entry = 0x140000UL + (ulong)i * 0x1000;
            U32(0x130000UL + (ulong)i * 16, 0xfffffffe);
            U64(0x130008UL + (ulong)i * 16, entry);
            U32(entry + 0x24, 7); U64(entry + 0x30, 0);
            U64(entry + 0x90, i == 0 ? unit : 0x180000);
        }
        U32(0x140018, 0x2b61676c); U32(0x180058, 851993);
        var reader = new WarcraftCombatReader(Read, module, 0x4000000);
        var state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x68303831, 0, 0, 1);
        Assert.NotNull(state);
        Assert.Equal(self, state.EngineHandle);
        Assert.Equal(851993U, state.CurrentOrderId);
        Assert.Equal(1.2f, state.BaseAttackCooldown);
        U64(unit + 0x500, ulong.MaxValue);
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x68303831, 0, 0, 1);
        Assert.NotNull(state);
        Assert.Equal(0U, state.CurrentOrderId);
        U64(0x140030, 1);
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x68303831, 0, 0, 1);
        Assert.NotNull(state);
        Assert.Null(state.EngineHandle);
        Assert.Null(state.CurrentOrderId);
        Assert.Null(state.BaseAttackCooldown);
    }
}
