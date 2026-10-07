using System.ComponentModel;

namespace OrandOverlay;

internal sealed class WarcraftCombatReader(Func<ulong, int, byte[]> read, ulong moduleBase, int moduleSize)
{
    private readonly RouteQuestMemory _memory = new(read);
    private readonly WarcraftHandleResolver _handles = new(read, moduleBase);
    private readonly WarcraftStateReader _states = new(read, moduleBase);
    private readonly WarcraftHelperReader _spells = new(read, moduleBase, moduleSize);

    internal CombatUnitState? Read(string version, string mapHash, ulong unit, uint rawcode,
        byte owner, byte localOwner, int sampleId)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            localOwner > 3 || moduleSize < 0x288 || (owner != localOwner && owner is not (5 or 6 or 7)))
            return null;
        try
        {
            var first = Snapshot(unit, rawcode, owner, localOwner, sampleId);
            var second = Snapshot(unit, rawcode, owner, localOwner, sampleId);
            if (first is null || first != second) return null;
            var abilities = new WarcraftBulletAbilityReader(read, moduleBase, moduleSize)
                .Read(version, mapHash, unit, rawcode, owner, localOwner);
            if (abilities is not null && (second!.EngineHandle is null ||
                second.EngineHandle != abilities.EngineHandle || second.Rawcode != abilities.Rawcode ||
                second.Owner != abilities.Owner)) return null;
            // The inner reader's stability does not fence the surrounding combat lifetime/state.
            if (Snapshot(unit, rawcode, owner, localOwner, sampleId) != second) return null;
            return second! with { BulletAbilities = abilities };
        }
        catch (Exception error) when (error is InvalidDataException or Win32Exception or OverflowException)
        {
            return null;
        }
    }

    private CombatUnitState? Snapshot(ulong unit, uint rawcode, byte owner, byte localOwner, int sampleId)
    {
        if (!Identity(unit, rawcode, owner)) return null;
        var head = U64(checked(unit + 0x558));
        var handle = head;
        var visited = new HashSet<ulong>();
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        HelperAbilityState? mirror = null;
        HelperAbilityState? uncommonSale = null;
        HelperAbilityState? ancientShip = null;
        while (((uint)handle & (uint)(handle >> 32)) != uint.MaxValue)
        {
            if (visited.Count >= 256 || _handles.Object(handle) is not { } ability || !visited.Add(ability))
                return null;
            var code = RouteQuestMemory.Rawcode(unchecked((int)U32(checked(ability + 0x70))));
            if (code is "A901" or "A902" or "A903" or "A904" or "A911" or "A912" or
                "A04M" or "B06P" or "A114" or "A115" or "A0B8" or "A0KB" or "A09R" or "A0T7" or "A0T6" or "A0BA")
            {
                var level = checked((int)U32(checked(ability + 0x9c)) + 1);
                if (level <= 0 || !levels.TryAdd(code, level)) return null;
                if (code == "A114" && owner == localOwner && rawcode == 0x68303853)
                    mirror = new(code, level, _spells.Cooldown(ability));
                if (code == "A0B8" && level == 1 && owner == localOwner &&
                    BulletGuideUncommonSalePolicy.IsSaleUnit(RouteQuestMemory.Rawcode(unchecked((int)rawcode))))
                    uncommonSale = new(code, level, _spells.Cooldown(ability));
                if (code == "A0KB" && level == 1 && owner == localOwner && rawcode == 0x68303559)
                    ancientShip = new(code, level, _spells.Cooldown(ability));
            }
            handle = U64(checked(ability + 0x58));
        }
        var boss = levels.GetValueOrDefault("A902") > 0 || levels.GetValueOrDefault("A911") > 0;
        CombatUnitKind kind;
        byte? lane = null;
        if (owner == localOwner)
            kind = rawcode == 0x68303831 ? CombatUnitKind.Bullet : CombatUnitKind.LocalUnit;
        else if (owner == 6)
        {
            var laneLevel = levels.GetValueOrDefault("A901");
            if (laneLevel is < 1 or > 4 || laneLevel - 1 != localOwner) return null;
            lane = (byte)(laneLevel - 1);
            kind = boss ? CombatUnitKind.LaneBoss : CombatUnitKind.LaneMonster;
        }
        else if (owner == 7)
        {
            if (levels.GetValueOrDefault("A912") > 0) kind = CombatUnitKind.RecipeExemplar;
            else if (levels.GetValueOrDefault("A0BA") > 0 || rawcode is 0x68303143 or 0x68303135)
                kind = CombatUnitKind.WildcardExemplar;
            else return null;
        }
        else if (levels.GetValueOrDefault("A904") > 0)
            kind = CombatUnitKind.Story;
        else if (boss)
            kind = CombatUnitKind.SharedBoss;
        else return null;
        var lifeHandle = U64(checked(unit + 0x258));
        var positionHandle = U64(checked(unit + 0x3b8));
        var life = _states.Regeneration(lifeHandle);
        var position = _states.Position(positionHandle);
        var armor = BitConverter.ToSingle(_memory.Bytes(checked(unit + 0x2e8), 4));
        var armorLevel = levels.GetValueOrDefault("A04M");
        int? armorStacks = armorLevel <= 76 ? Math.Max(0, armorLevel - 1) : null;
        var selfHandle = U64(checked(unit + 0x18));
        ulong? engineHandle = null;
        uint? orderId = null;
        float? baseCooldown = null;
        if (_handles.Entry(selfHandle) is { } agent &&
            U32(checked(agent + 0x18)) == 0x2b61676c && U64(checked(agent + 0x30)) == 0 &&
            U64(checked(agent + 0x90)) == unit)
        {
            engineHandle = selfHandle;
            var orderHandle = U64(checked(unit + 0x500));
            if (((uint)orderHandle & (uint)(orderHandle >> 32)) == uint.MaxValue)
                orderId = 0;
            else if (_handles.Object(orderHandle) is { } order)
                orderId = U32(checked(order + 0x58));
            var attack = U64(checked(unit + 0x5c0));
            var cooldown = attack == 0 ? 0 : BitConverter.ToSingle(_memory.Bytes(checked(attack + 0x200), 4));
            if (float.IsFinite(cooldown) && cooldown >= 0) baseCooldown = cooldown;
            if (U64(checked(unit + 0x18)) != selfHandle || _handles.Object(selfHandle) != unit)
                return null;
        }
        var selectedModes = new[] { "A09R", "A0T7", "A0T6" }
            .Where(code => levels.GetValueOrDefault(code) > 0).ToArray();
        BlackMariaMode? blackMaria = rawcode == 0x68303455 && owner == localOwner &&
            life is { Current: > 0 } && selectedModes.Length == 1
            ? selectedModes[0] switch { "A09R" => BlackMariaMode.Stun, "A0T7" => BlackMariaMode.Slow, _ => BlackMariaMode.Burn }
            : null;
        return Identity(unit, rawcode, owner) && U64(checked(unit + 0x558)) == head &&
            U64(checked(unit + 0x258)) == lifeHandle && U64(checked(unit + 0x3b8)) == positionHandle
            ? new(sampleId, RouteQuestMemory.Rawcode(unchecked((int)rawcode)), owner, lane, kind,
                position, life?.Current, life?.Maximum, armorStacks,
                levels.GetValueOrDefault("A903") > 0, levels.GetValueOrDefault("B06P") > 0)
            {
                BlackMariaSelectedMode = blackMaria,
                LegendMarked = levels.GetValueOrDefault("A912") > 0,
                MirrorCopy = levels.GetValueOrDefault("A115") > 0,
                MirrorAbility = mirror,
                UncommonSaleAbility = uncommonSale,
                AncientShipAbility = ancientShip,
                NativeArmor = float.IsFinite(armor) ? armor : null,
                EngineHandle = engineHandle,
                CurrentOrderId = orderId,
                BaseAttackCooldown = baseCooldown
            }
            : null;
    }

    private bool Identity(ulong unit, uint rawcode, byte owner)
    {
        if (!ReadOnlyProcessMemory.IsPlausibleUserAddress(unit) ||
            U32(checked(unit + 0x178)) != rawcode || _memory.Bytes(checked(unit + 0x1c0), 1)[0] != owner)
            return false;
        var vtable = U64(unit);
        return vtable >= moduleBase && checked(vtable + 0x288) <= checked(moduleBase + (ulong)moduleSize) &&
            U64(checked(vtable + 0x178)) == checked(moduleBase + 0x1163ad0) &&
            U64(checked(vtable + 0x278)) == checked(moduleBase + 0x1163a00) &&
            U64(checked(vtable + 0x280)) == checked(moduleBase + 0x1163a20);
    }

    private uint U32(ulong address) => BitConverter.ToUInt32(_memory.Bytes(address, 4));
    private ulong U64(ulong address) => BitConverter.ToUInt64(_memory.Bytes(address, 8));
}
