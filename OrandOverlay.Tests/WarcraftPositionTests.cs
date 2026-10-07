using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftPositionTests
{
    [Fact]
    public void ConvertsNativePositionWithWorldOriginAndSimulationVelocity()
    {
        var bytes = new Dictionary<ulong, byte>();
        void Put(ulong address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i];
        }
        void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        void Real(ulong address, float value) => Put(address, BitConverter.GetBytes(value));
        byte[] Read(ulong address, int length) => Enumerable.Range(0, length)
            .Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
        const ulong module = 0x400000, entry = 0x160000, clock = 0x190018, world = 0x1a0000;
        U64(module + 0x2b808c0, 0x120000);
        U32(0x120030, 1); U64(0x120018, 0x130000);
        U32(0x130000, 0xfffffffe); U64(0x130008, entry);
        U32(entry + 0x24, 7); U32(entry + 0x20, 0);
        Real(entry + 0xc8, 10); U32(entry + 0xcc, 0);
        Real(entry + 0xd0, 216); Real(entry + 0xd4, 555.5f);
        Real(entry + 0xd8, 0); Real(entry + 0xdc, 0);
        U64(module + 0x2b80848, clock - 0x18);
        Real(clock + 0x70, 13); U32(clock + 0x74, 0); Real(clock + 0x78, 300);
        Real(module + 0x2a9704c, 0.0001f);
        U64(module + 0x2a96c48, world ^ 0x2b29df2be4f74917);
        Real(world + 0xe0, -13312); Real(world + 0xe4, -12800);
        var reader = new WarcraftStateReader(Read, module);
        // Same frozen fields produced (-5888, 4464) through the copied native coordinate functions.
        Assert.Equal(new UnitPosition(-5888, 4464), reader.Position(7UL << 32));
        Real(entry + 0xd8, 2); Real(entry + 0xdc, -1);
        Assert.Equal(new UnitPosition(-5696, 4368), reader.Position(7UL << 32));
        U32(entry + 0x24, 8);
        Assert.Null(reader.Position(7UL << 32));
    }
}
