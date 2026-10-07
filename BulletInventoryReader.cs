using System.ComponentModel;
using System.Text;

namespace OrandOverlay;

internal sealed class BulletInventoryReader(Func<ulong, int, byte[]> read, ulong moduleBase, int moduleSize)
{
    private readonly RouteQuestMemory _memory = new(read);
    private readonly WarcraftHandleResolver _handles = new(read, moduleBase);

    internal BulletUpgradeCounts? Read(string version, string mapHash, ulong unit, byte owner)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 || owner > 3)
            return null;
        try
        {
            var first = Snapshot(unit, owner);
            var second = Snapshot(unit, owner);
            return first is not null && first == second ? second : null;
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return null;
        }
    }

    private BulletUpgradeCounts? Snapshot(ulong unit, byte owner)
    {
        if (!IsClass(unit, "CUnit") || U32(checked(unit + 0x178)) != 0x68303831 ||
            _memory.Bytes(checked(unit + 0x1c0), 1)[0] != owner) return null;
        var inventory = U64(checked(unit + 0x5a0));
        if (!IsClass(inventory, "CAbilityInventory") || U32(checked(inventory + 0xd0)) is < 5 or > 6)
            return null;
        var items = new Dictionary<int, (string, int)>();
        foreach (var slot in new[] { 0, 2, 4 })
        {
            var handle = U64(checked(inventory + 0xd4 + (ulong)slot * 12));
            if (_handles.Object(handle) is not { } item || !IsClass(item, "CItem")) return null;
            var rawcode = RouteQuestMemory.Rawcode(unchecked((int)U32(checked(item + 0x70))));
            var charges = unchecked((int)U32(checked(item + 0x8d0)));
            items[slot] = (rawcode, charges);
        }
        return U32(checked(unit + 0x178)) == 0x68303831 &&
            _memory.Bytes(checked(unit + 0x1c0), 1)[0] == owner &&
            U64(checked(unit + 0x5a0)) == inventory
            ? BulletUpgradeCounts.FromItems(items) : null;
    }

    private bool IsClass(ulong instance, string expected)
    {
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(instance)) return false;
        var vtable = U64(instance);
        if (vtable < moduleBase + 8 || vtable >= checked(moduleBase + (ulong)moduleSize)) return false;
        var locator = U64(vtable - 8);
        if (U32(locator) != 1) return false;
        var rva = U32(checked(locator + 12));
        if (rva >= moduleSize - 80) return false;
        var bytes = _memory.Bytes(checked(moduleBase + rva + 16), 64);
        var end = Array.IndexOf(bytes, (byte)0);
        return end > 0 && Encoding.ASCII.GetString(bytes, 0, end) == ".?AV" + expected + "@@";
    }

    private uint U32(ulong address) => BitConverter.ToUInt32(_memory.Bytes(address, 4));
    private ulong U64(ulong address) => BitConverter.ToUInt64(_memory.Bytes(address, 8));
}
