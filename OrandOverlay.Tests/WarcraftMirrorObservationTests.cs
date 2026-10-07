using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftMirrorObservationTests
{
    [Fact]
    public void ReadsLegendExemplarAndOwnedBruleeWithoutAdmittingAnotherPlayer()
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
        const ulong unit = 0x100000, module = 0x400000, ability = 0x180000;
        U64(unit, 0x401000);
        U64(0x401178, module + 0x1163ad0); U64(0x401278, module + 0x1163a00);
        U64(0x401280, module + 0x1163a20);
        U32(unit + 0x178, 0x68303338); Put(unit + 0x1c0, [7]);
        U64(unit + 0x258, ulong.MaxValue); U64(unit + 0x3b8, ulong.MaxValue);
        U64(unit + 0x558, 9UL << 32);
        U64(module + 0x2b808c0, 0x120000);
        U32(0x120030, 1); U64(0x120018, 0x130000);
        U32(0x130000, 0xfffffffe); U64(0x130008, 0x140000);
        U32(0x140024, 9); U64(0x140030, 0); U64(0x140090, ability);
        U64(ability, 0x404000); U64(0x404648, module + 0x6515d0);
        U32(ability + 0x70, 0x41393132); U32(ability + 0x9c, 0);
        U32(ability + 0x38, 0x10); U64(ability + 0x58, ulong.MaxValue);
        var reader = new WarcraftCombatReader(Read, module, 0x4000000);
        var target = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x68303338, 7, 0, 1);
        Assert.NotNull(target);
        Assert.Equal(CombatUnitKind.RecipeExemplar, target.Kind);
        Assert.True(target.LegendMarked);
        Put(unit + 0x1c0, [1]);
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x68303338, 1, 0, 1));
        Put(unit + 0x1c0, [0]); U32(unit + 0x178, 0x68303853);
        U32(ability + 0x70, 0x41313134);
        var brulee = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0x68303853, 0, 0, 2);
        Assert.NotNull(brulee);
        Assert.Equal(new HelperAbilityState("A114", 1, 0), brulee.MirrorAbility);
    }
}
