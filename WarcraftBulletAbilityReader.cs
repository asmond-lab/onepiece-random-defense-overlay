using System.ComponentModel;
using System.Text;

namespace OrandOverlay;

internal sealed class WarcraftBulletAbilityReader(Func<ulong, int, byte[]> read, ulong moduleBase, int moduleSize)
{
    private readonly RouteQuestMemory memory = new(read);
    private readonly WarcraftHandleResolver handles = new(read, moduleBase);

    internal BulletAbilityObservation? Read(string version, string mapHash, ulong unit, uint rawcode, byte owner, byte localOwner)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 || localOwner > 3 ||
            (owner != localOwner && owner != 7) || moduleSize < 0x288) return null;
        try
        {
            var first = Snapshot(unit, rawcode, owner, localOwner);
            var second = Snapshot(unit, rawcode, owner, localOwner);
            return first is not null && second is { } stable && first == second ? stable.Observation : null;
        }
        catch (Exception e) when (e is InvalidDataException or Win32Exception or OverflowException) { return null; }
    }

    private (BulletAbilityObservation Observation, string Identity)? Snapshot(ulong unit, uint rawcode, byte owner, byte localOwner)
    {
        if (!Identity(unit, rawcode, owner)) return null;
        var self = U64(unit + 0x18);
        if (handles.Entry(self) is not { } agent || U32(agent + 0x18) != 0x2b61676c ||
            U64(agent + 0x30) != 0 || U64(agent + 0x90) != unit) return null;
        var head = U64(unit + 0x558);
        var link = head;
        var visited = new HashSet<ulong>();
        var levels = new Dictionary<string, uint>(StringComparer.Ordinal);
        float? cooldown = null;
        var signature = new StringBuilder().Append(U64(unit)).Append(':').Append(self).Append(':').Append(agent).Append(':').Append(head);
        while (((uint)link & (uint)(link >> 32)) != uint.MaxValue)
        {
            if (visited.Count >= 256 || handles.Entry(link) is not { } entry ||
                handles.Object(link) is not { } ability || !visited.Add(ability)) return null;
            var code = RouteQuestMemory.Rawcode(unchecked((int)U32(ability + 0x70)));
            var level = U32(ability + 0x9c);
            var next = U64(ability + 0x58);
            signature.Append('|').Append(link).Append(':').Append(entry).Append(':').Append(ability).Append(':').Append(code).Append(':').Append(level).Append(':').Append(next);
            if (!levels.TryAdd(code, level)) return null;
            if (code == "A09C" && rawcode == 0x48304334 && owner == localOwner)
            {
                try { cooldown = new WarcraftHelperReader(read, moduleBase, moduleSize).Cooldown(ability); }
                catch (Exception e) when (e is InvalidDataException or Win32Exception or OverflowException) { cooldown = null; }
            }
            if (handles.Entry(link) != entry || handles.Object(link) != ability || U32(ability + 0x70) != MemoryCode(code) ||
                U32(ability + 0x9c) != level || U64(ability + 0x58) != next) return null;
            link = next;
        }
        if (!Identity(unit, rawcode, owner) || U64(unit + 0x18) != self || handles.Object(self) != unit || U64(unit + 0x558) != head) return null;
        var raw = RouteQuestMemory.Rawcode(unchecked((int)rawcode));
        var a134 = levels.ContainsKey("A134"); var a13c = levels.ContainsKey("A13C");
        var state = raw is "h0A3" or "h0A1" or "h0A0" or "h09Y"
            ? GreenBloodMarkerState.IntrinsicSeraphim
            : a134 && a13c ? GreenBloodMarkerState.AppliedMarkers
            : a134 || a13c ? GreenBloodMarkerState.Partial : GreenBloodMarkerState.Absent;
        var carrier = raw == "H0C4" && owner == localOwner;
        int? remaining = !carrier ? null : !levels.TryGetValue("A09C", out var stored) ? 0 : stored < 4 ? (int)stored + 1 : null;
        return (new(self, raw, owner, state, a134, a13c, levels.ContainsKey("A912"), levels.ContainsKey("A07N"))
        {
            A09CRemaining = remaining,
            A13APresent = carrier ? levels.ContainsKey("A13A") : null,
            A09CCooldown = remaining is > 0 ? cooldown : null,
            A09CTargetEligible = levels.ContainsKey("A0BA") || raw is "h01C" or "h015"
        }, signature.ToString());
    }

    private bool Identity(ulong unit, uint rawcode, byte owner)
    {
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(unit) || U32(unit + 0x178) != rawcode || memory.Bytes(unit + 0x1c0, 1)[0] != owner) return false;
        var vtable = U64(unit);
        return vtable >= moduleBase && checked(vtable + 0x288) <= checked(moduleBase + (ulong)moduleSize) &&
            U64(vtable + 0x178) == moduleBase + 0x1163ad0 && U64(vtable + 0x278) == moduleBase + 0x1163a00 &&
            U64(vtable + 0x280) == moduleBase + 0x1163a20;
    }
    private static uint MemoryCode(string code) => code.Aggregate(0U, (value, c) => (value << 8) | c);
    private uint U32(ulong address) => BitConverter.ToUInt32(memory.Bytes(address, 4));
    private ulong U64(ulong address) => BitConverter.ToUInt64(memory.Bytes(address, 8));
}
