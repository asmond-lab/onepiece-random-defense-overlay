using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftHelperReaderTests
{
    [Fact]
    public void ReadsRegeneratedManaAndActiveCooldownWithoutTreatingBaseAsCurrent()
    {
        var bytes = new Dictionary<ulong, byte>();
        void Put(ulong address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i];
        }
        void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        void Real(ulong address, float value) => Put(address, BitConverter.GetBytes(value));
        Action<ulong, int>? afterRead = null;
        byte[] Read(ulong address, int length)
        {
            var result = Enumerable.Range(0, length)
                .Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
            afterRead?.Invoke(address, length);
            return result;
        }
        const ulong unit = 0x100000, mana = 0x160000, ability = 0x180000, clock = 0x190018;
        U64(unit, 0x401000);
        U64(0x400ff8, 0x402000);
        U32(0x402000, 1); U32(0x40200c, 0x3000);
        Put(0x403010, Encoding.ASCII.GetBytes(".?AVCUnit@@\0"));
        U32(unit + 0x178, 0x68303841);
        Put(unit + 0x1c0, [0]);
        U64(unit + 0x2a8, (7UL << 32) | 1);
        U64(unit + 0x558, 7UL << 32);
        U64(0x400000 + 0x2b808c0UL, 0x120000);
        U32(0x120030, 2); U64(0x120018, 0x130000);
        U32(0x130000, 0xfffffffe); U64(0x130008, 0x170000);
        U32(0x130010, 0xfffffffe); U64(0x130018, mana);
        U32(0x170024, 7); U64(0x170030, 0); U64(0x170090, ability);
        U32(mana + 0x24, 7); U32(mana + 0x20, 1);
        Real(mana + 0xc8, 6.5f); U32(mana + 0xcc, 0);
        Real(mana + 0xd0, 15); Real(mana + 0xd4, 0.3f);
        Real(mana + 0xdc, 0); Real(mana + 0xe0, 10000);
        U64(0x400000 + 0x2b80848UL, clock - 0x18);
        Real(clock + 0x70, 17.5f); U32(clock + 0x74, 0); Real(clock + 0x78, 300);
        Real(0x400000 + 0x2a9704cUL, 0.0001f);
        U64(ability, 0x404000); U64(0x404648, 0x400000 + 0x6515d0UL);
        U32(ability + 0x70, 0x41303832); U32(ability + 0x9c, 0);
        U32(ability + 0x38, 0x10); U64(ability + 0x58, ulong.MaxValue);
        var reader = new WarcraftHelperReader(Read, 0x400000, 0x4000000);
        var state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0);
        Assert.NotNull(state);
        Assert.Equal(18.3f, state.Mana);
        Assert.Equal(10000, state.MaximumMana);
        Assert.Equal(new HelperAbilityState("A082", 1, 0), Assert.Single(state.Abilities));
        U32(ability + 0x38, 0x210);
        U64(ability + 0x170, 0x405000);
        U64(0x405028, 0x400000 + 0x3eabe0UL);
        U64(ability + 0x180, 0x1a0000);
        Real(0x1a0008, 25); U64(0x1a0010, clock);
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0);
        Assert.NotNull(state);
        Assert.Equal(7.5f, Assert.Single(state.Abilities).CooldownRemaining);
        Real(mana + 0xc8, 299); Real(clock + 0x70, 2); U32(clock + 0x74, 1);
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0);
        Assert.NotNull(state);
        Assert.Equal(15.9f, state.Mana);
        U32(mana + 0x20, 0x80000001); Real(mana + 0xc8, 6.5f);
        const ulong alternateClock = 0x1900a0;
        Real(alternateClock + 0x70, 12.5f);
        U32(alternateClock + 0x74, 0); Real(alternateClock + 0x78, 300);
        state = reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0);
        Assert.NotNull(state);
        Assert.Equal(16.8f, state.Mana);
        afterRead = (address, length) =>
        {
            if (address == alternateClock + 0x70 && length == 12)
            {
                Real(alternateClock + 0x70, 13.5f);
                afterRead = null;
            }
        };
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0));
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 1));
        Assert.Null(reader.Read("other", RouteQuestCatalog.MapScriptSha256, unit, 0));
        U32(mana + 0x24, 8);
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0));
    }
}
