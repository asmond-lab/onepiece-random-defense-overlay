using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void ProbeNativeUnits(string output, RecognitionResult observation)
    {
        if (observation.State is not (RecognitionState.Ready or RecognitionState.Waiting) ||
            observation.Diagnostics.ProcessId is not { } pid ||
            observation.VerifiedLocalPlayerSlot is not { } owner)
            throw new InvalidOperationException("Current verified ownership required.");
        using var process = Process.GetProcessById(pid);
        var module = process.MainModule ?? throw new InvalidOperationException("Module missing.");
        if (process.StartTime > observation.CapturedAt) throw new InvalidOperationException("Process changed.");
        using var memory = ReadOnlyProcessMemory.Open(pid);
        var moduleBase = (ulong)module.BaseAddress.ToInt64();
        var handles = new WarcraftHandleResolver(memory.ReadAvailable, moduleBase);
        var states = new WarcraftStateReader(memory.ReadAvailable, moduleBase);
        var combatReader = new WarcraftCombatReader(memory.ReadAvailable, moduleBase, module.ModuleMemorySize);
        var rows = new List<object>();
        foreach (var (rawcode, pointers) in observation.NativeUnitPointers)
        {
            if (pointers.Count != 1) continue;
            var unit = pointers[0];
            if (memory.ReadUInt32(unit + 0x178) != rawcode || memory.ReadByte(unit + 0x1c0) != owner) continue;
            var vtable = memory.ReadUInt64(unit);
            var inventory = memory.ReadUInt64(unit + 0x5a0);
            var abilities = new List<object>();
            var visited = new HashSet<ulong>();
            var ability = handles.Object(memory.ReadUInt64(unit + 0x558));
            while (ability is { } pointer && visited.Add(pointer) && visited.Count <= 256)
            {
                var code = RouteQuestMemory.Rawcode(unchecked((int)memory.ReadUInt32(pointer + 0x70)));
                if (code is "A082" or "A07Z" or "A07X" or "A0BY" or "A114")
                {
                    var abilityVtable = memory.ReadUInt64(pointer);
                    abilities.Add(new { Code = code, Pointer = $"0x{pointer:X}",
                        Level = memory.ReadInt32(pointer + 0x9c) + 1,
                        CooldownFunction = $"0x{memory.ReadUInt64(abilityVtable + 0x648):X}" });
                }
                ability = handles.Object(memory.ReadUInt64(pointer + 0x58));
            }
            var mana = handles.Entry(memory.ReadUInt64(unit + 0x2a8));
            object? manaFields = mana is { } state ? new
            {
                Entry = $"0x{state:X}", Domain = memory.ReadInt32(state + 0x20),
                LastTime = memory.ReadUInt32(state + 0xc8), LastMode = memory.ReadUInt32(state + 0xcc),
                Base = BitConverter.ToSingle(memory.Read(state + 0xd0, 4)),
                Rate = BitConverter.ToSingle(memory.Read(state + 0xd4, 4)),
                Minimum = BitConverter.ToSingle(memory.Read(state + 0xdc, 4)),
                Maximum = BitConverter.ToSingle(memory.Read(state + 0xe0, 4))
            } : null;
            var life = states.Regeneration(memory.ReadUInt64(unit + 0x258));
            var position = states.Position(memory.ReadUInt64(unit + 0x3b8));
            var combat = combatReader.Read(observation.Diagnostics.ProcessVersion, RouteQuestCatalog.MapScriptSha256,
                unit, rawcode, owner, owner, rows.Count);
            rows.Add(new
            {
                Rawcode = RouteQuestMemory.Rawcode(unchecked((int)rawcode)), Unit = $"0x{unit:X}", Owner = owner,
                PositionFunction = $"0x{memory.ReadUInt64(vtable + 0x178):X}",
                LifeFunction = $"0x{memory.ReadUInt64(vtable + 0x278):X}",
                MaxLifeFunction = $"0x{memory.ReadUInt64(vtable + 0x280):X}",
                Inventory = $"0x{inventory:X}", Mana = manaFields, Abilities = abilities,
                VerifiedPosition = position, VerifiedLife = life?.Current, VerifiedMaximumLife = life?.Maximum,
                VerifiedNativeArmor = combat?.NativeArmor,
                VerifiedEngineHandle = combat?.EngineHandle,
                VerifiedCurrentOrderId = combat?.CurrentOrderId,
                VerifiedBaseAttackCooldown = combat?.BaseAttackCooldown,
                StableOwner = memory.ReadUInt32(unit + 0x178) == rawcode && memory.ReadByte(unit + 0x1c0) == owner
            });
        }
        WriteEvidenceText(Path.Combine(output, "native-unit-probe.json"),
            JsonSerializer.Serialize(new { Kind = "read-only-candidate-fields-not-yet-production-values",
                ProcessId = pid, ModuleBase = $"0x{moduleBase:X}", Rows = rows,
                VerifiedHelperState = observation.HelperState, CombatObservations = observation.CombatObservations },
                new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"NATIVE_UNIT_PROBE_DONE rows={rows.Count} helperKnown={observation.HelperState is not null} combat={observation.CombatObservations.Length}");
    }
}
