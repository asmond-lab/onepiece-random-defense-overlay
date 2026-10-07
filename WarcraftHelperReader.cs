using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;

namespace OrandOverlay;

internal sealed class WarcraftHelperReader(Func<ulong, int, byte[]> read, ulong moduleBase, int moduleSize)
{
    private readonly RouteQuestMemory _memory = new(read);
    private readonly WarcraftHandleResolver _handles = new(read, moduleBase);
    private readonly WarcraftStateReader _states = new(read, moduleBase);

    internal HelperUnitState? Read(string version, string mapHash, ulong unit, byte owner)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            owner > 3 || moduleSize < 80) return null;
        try
        {
            var first = Snapshot(unit, owner);
            var second = Snapshot(unit, owner);
            return first is not null && second is not null && first.Mana == second.Mana &&
                first.MaximumMana == second.MaximumMana && first.Abilities.SequenceEqual(second.Abilities)
                ? second : null;
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return null;
        }
    }

    private HelperUnitState? Snapshot(ulong unit, byte owner)
    {
        if (!IsHelper(unit, owner)) return null;
        var manaHandle = U64(checked(unit + 0x2a8));
        if (_states.Regeneration(manaHandle) is not { } mana) return null;
        var head = U64(checked(unit + 0x558));
        var handle = head;
        var visited = new HashSet<ulong>();
        var abilities = new List<HelperAbilityState>();
        while (((uint)handle & (uint)(handle >> 32)) != uint.MaxValue)
        {
            if (visited.Count >= 256 || _handles.Object(handle) is not { } ability || !visited.Add(ability))
                return null;
            var code = RouteQuestMemory.Rawcode(unchecked((int)U32(checked(ability + 0x70))));
            if (code is "A082" or "A07Z" or "A07X" or "A0BY")
            {
                var level = checked((int)U32(checked(ability + 0x9c)) + 1);
                if (level <= 0 || abilities.Any(value => value.Rawcode == code)) return null;
                abilities.Add(new(code, level, Cooldown(ability)));
            }
            handle = U64(checked(ability + 0x58));
        }
        return IsHelper(unit, owner) && U64(checked(unit + 0x2a8)) == manaHandle &&
            U64(checked(unit + 0x558)) == head
            ? new(mana.Current, mana.Maximum, abilities.OrderBy(value => value.Rawcode, StringComparer.Ordinal).ToImmutableArray())
            : null;
    }

    internal float? Cooldown(ulong ability)
    {
        var vtable = U64(ability);
        if (!InModule(vtable, 0x650) || U64(checked(vtable + 0x648)) != checked(moduleBase + 0x6515d0))
            return null;
        var flags = U32(checked(ability + 0x38));
        float remaining = 0;
        if ((flags & 0x600) == 0x200)
        {
            var timer = checked(ability + 0x170);
            var timerVtable = U64(timer);
            if (!InModule(timerVtable, 0x30) ||
                U64(checked(timerVtable + 0x28)) != checked(moduleBase + 0x3eabe0)) return null;
            var record = U64(checked(timer + 0x10));
            if (record != 0)
            {
                var end = Real(checked(record + 8));
                var clock = U64(checked(record + 0x10));
                var now = Real(checked(clock + 0x70));
                if (!float.IsFinite(end) || !float.IsFinite(now)) return null;
                remaining = WarcraftRealMath.Subtract(end, now);
                if (U64(checked(timer + 0x10)) != record || Real(checked(record + 8)) != end ||
                    U64(checked(record + 0x10)) != clock || Real(checked(clock + 0x70)) != now) return null;
            }
        }
        return float.IsFinite(remaining) && flags == U32(checked(ability + 0x38)) ? remaining : null;
    }

    private bool IsHelper(ulong unit, byte owner)
    {
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(unit) ||
            U32(checked(unit + 0x178)) != 0x68303841 ||
            _memory.Bytes(checked(unit + 0x1c0), 1)[0] != owner) return false;
        var vtable = U64(unit);
        if (vtable < moduleBase + 8 || !InModule(vtable - 8, 8)) return false;
        var locator = U64(vtable - 8);
        if (!InModule(locator, 16) || U32(locator) != 1) return false;
        var name = checked(moduleBase + U32(checked(locator + 12)) + 16);
        if (!InModule(name, 64)) return false;
        var bytes = _memory.Bytes(name, 64);
        var end = Array.IndexOf(bytes, (byte)0);
        return end > 0 && Encoding.ASCII.GetString(bytes, 0, end) == ".?AVCUnit@@";
    }

    private bool InModule(ulong address, ulong length) =>
        address >= moduleBase && checked(address + length) <= checked(moduleBase + (ulong)moduleSize);
    private uint U32(ulong address) => BitConverter.ToUInt32(_memory.Bytes(address, 4));
    private ulong U64(ulong address) => BitConverter.ToUInt64(_memory.Bytes(address, 8));
    private float Real(ulong address) => BitConverter.ToSingle(_memory.Bytes(address, 4));
}
