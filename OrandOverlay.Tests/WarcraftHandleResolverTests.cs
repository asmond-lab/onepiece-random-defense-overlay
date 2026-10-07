using Xunit;

namespace OrandOverlay.Tests;

public sealed class WarcraftHandleResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedIndexAndGenerationResolveOnlyLiveObject(bool alternate)
    {
        var bytes = new Dictionary<ulong, byte>();
        void Put(ulong address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i];
        }
        byte[] Read(ulong address, int length) => Enumerable.Range(0, length)
            .Select(i => bytes[address + (ulong)i]).ToArray();
        Put(0x400000 + 0x2B808C0UL, BitConverter.GetBytes(0x100000UL));
        Put(0x100000UL + (alternate ? 0x68UL : 0x30UL), BitConverter.GetBytes(4U));
        Put(0x100000UL + (alternate ? 0x50UL : 0x18UL), BitConverter.GetBytes(0x200000UL));
        Put(0x200030, BitConverter.GetBytes(0xfffffffeU));
        Put(0x200038, BitConverter.GetBytes(0x300000UL));
        Put(0x300024, BitConverter.GetBytes(7U));
        Put(0x300030, BitConverter.GetBytes(0UL));
        Put(0x300090, BitConverter.GetBytes(0x500000UL));
        var handle = (7UL << 32) | (alternate ? 0x80000003UL : 3UL);
        var resolver = new WarcraftHandleResolver(Read, 0x400000);
        Assert.Equal(0x300000UL, resolver.Entry(handle));
        Assert.Equal(0x500000UL, resolver.Object(handle));
        Assert.Null(resolver.Entry((8UL << 32) | (uint)handle));
        Put(0x300030, BitConverter.GetBytes(1UL));
        Assert.Null(resolver.Object(handle));
        Assert.Null(resolver.Entry((7UL << 32) | (alternate ? 0x80000004UL : 4UL)));
    }
}
