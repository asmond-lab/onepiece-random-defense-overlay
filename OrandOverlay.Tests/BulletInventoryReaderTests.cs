using System.Text;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class BulletInventoryReaderTests
{
    [Fact]
    public void ReadsOnlyOwnedBulletInventoryWithValidItemHandles()
    {
        var bytes = new Dictionary<ulong, byte>();
        void Put(ulong address, byte[] value)
        {
            for (var i = 0; i < value.Length; i++) bytes[address + (ulong)i] = value[i];
        }
        void U32(ulong address, uint value) => Put(address, BitConverter.GetBytes(value));
        void U64(ulong address, ulong value) => Put(address, BitConverter.GetBytes(value));
        void Class(ulong instance, ulong vtable, ulong locator, uint nameRva, string name)
        {
            U64(instance, vtable); U64(vtable - 8, locator); U32(locator, 1); U32(locator + 12, nameRva);
            var nameBytes = new byte[64];
            Encoding.ASCII.GetBytes(".?AV" + name + "@@\0").CopyTo(nameBytes, 0);
            Put(0x400000UL + nameRva + 16, nameBytes);
        }
        Action<ulong>? afterRead = null;
        byte[] Read(ulong address, int length)
        {
            var result = Enumerable.Range(0, length)
                .Select(i => bytes.GetValueOrDefault(address + (ulong)i)).ToArray();
            afterRead?.Invoke(address);
            return result;
        }
        const ulong unit = 0x100000, inventory = 0x110000;
        Class(unit, 0x401000, 0x402000, 0x3000, "CUnit");
        U32(unit + 0x178, 0x68303831);
        Put(unit + 0x1c0, [0]);
        U64(unit + 0x5a0, inventory);
        Class(inventory, 0x404000, 0x405000, 0x6000, "CAbilityInventory");
        U32(inventory + 0xd0, 6);
        U64(0x400000 + 0x2B808C0UL, 0x120000);
        U32(0x120030, 3);
        U64(0x120018, 0x130000);
        for (var index = 0; index < 3; index++)
        {
            var entry = 0x140000UL + (ulong)index * 0x1000;
            var item = 0x150000UL + (ulong)index * 0x1000;
            U64(inventory + 0xd4 + (ulong)(index * 2 * 12), (11UL << 32) | (uint)index);
            U32(0x130000UL + (ulong)index * 16, 0xfffffffe);
            U64(0x130008UL + (ulong)index * 16, entry);
            U32(entry + 0x24, 11); U64(entry + 0x30, 0); U64(entry + 0x90, item);
            Class(item, 0x407000, 0x408000, 0x9000, "CItem");
            U32(item + 0x70, 0x49303931U + (uint)index);
            U32(item + 0x8d0, (uint)(14 + index));
        }
        var reader = new BulletInventoryReader(Read, 0x400000, 0x4000000);
        Assert.Equal(new BulletUpgradeCounts(14, 15, 16),
            reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0));
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 1));
        U32(0x141024, 12);
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0));
        U32(0x141024, 11);
        var finalItemReads = 0;
        afterRead = address =>
        {
            if (address == 0x1528d0 && ++finalItemReads == 2) Put(unit + 0x1c0, [1]);
        };
        Assert.Null(reader.Read("2.0.4.23745", RouteQuestCatalog.MapScriptSha256, unit, 0));
    }
}
