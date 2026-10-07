using System.ComponentModel;

namespace OrandOverlay;

public enum NativeNavigationStatus { Unknown, Unselected, Selected, Conflict }
public sealed record NativeNavigationSnapshot(NativeNavigationStatus Status, string? OptionId, string Detail)
{
    public static NativeNavigationSnapshot Unknown { get; } = new(NativeNavigationStatus.Unknown, null, "JP/SP 항법 native 미확인");
    public string Source => "Native JP/SP · verified local slot · two snapshots";
    public string? Resolve(string? userConfirmed) => Status == NativeNavigationStatus.Selected ? OptionId :
        Status is NativeNavigationStatus.Conflict or NativeNavigationStatus.Unselected ? null : userConfirmed;
}

internal sealed class WarcraftNavigationReader
{
    internal NativeNavigationSnapshot Read(Func<ulong, int, byte[]> read, string version,
        string mapHash, byte? localSlot, ulong? anchor, CancellationToken token = default)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            localSlot is null or > 3 || anchor is null) return NativeNavigationSnapshot.Unknown;
        try
        {
            var values = StableMapGlobals.Read(read, anchor.Value,
                [("JP", 9), ("SP", 9), ("cE", 13), ("Ly", 13)], localSlot.Value, token);
            var jp = values["JP"]; var sp = values["SP"];
            var selected = values["cE"]; var king = values["Ly"];
            if (jp is < 0 or > 3 || sp is < 0 or > 3 || selected is < 0 or > 1 || king is < 0 or > 1 ||
                jp > 0 && sp > 0 || jp > 0 && king != 1 || sp > 0 && king != 1 ||
                (jp > 0 || sp > 0) && selected != 1)
                return new(NativeNavigationStatus.Conflict, null, "JP/SP 항법 값·선택 flag 충돌 · 소비 권고 중지");
            var id = jp switch { 1 => "PathOfKings.MartialLaw", 2 => "PathOfKings.BountyHunter",
                3 => "PathOfKings.RoyalLoader", _ => sp switch { 1 => "AlliedForces.DoubleBenefit",
                    2 => "AlliedForces.EmergencyCall", 3 => "AlliedForces.TraitEngineering", _ => null } };
            return id is not null ? new(NativeNavigationStatus.Selected, id, "native JP/SP 선택 확인 · 현재 잔여 사용량은 별도 관측") :
                selected == 0 && king == 0 ? new(NativeNavigationStatus.Unselected, null, "native 항법 미선택") :
                NativeNavigationSnapshot.Unknown with { Detail = "다른 항법 family 또는 전이 상태 · JP/SP로 판정 불가" };
        }
        catch (Exception e) when (e is InvalidDataException or Win32Exception or OverflowException)
        { return NativeNavigationSnapshot.Unknown with { Detail = "JP/SP native 미확인 · " + e.Message }; }
    }
}

internal static class StableMapGlobals
{
    internal static Dictionary<string, int> Read(Func<ulong, int, byte[]> read, ulong anchor,
        (string Name, int Type)[] bindings, byte slot, CancellationToken token)
    {
        var first = Snapshot(); var second = Snapshot();
        if (!first.Trace.SequenceEqual(second.Trace)) throw new InvalidDataException("맵 변수 두 snapshot 변경 중");
        return second.Values;

        (Dictionary<string, int> Values, List<(ulong, string)> Trace) Snapshot()
        {
            var trace = new List<(ulong, string)>();
            var memory = new RouteQuestMemory((address, size) =>
            {
                token.ThrowIfCancellationRequested();
                var bytes = read(address, size); trace.Add((address, Convert.ToHexString(bytes))); return bytes;
            });
            var nodes = QuestGlobalDiscovery.FindRelated(memory, anchor, bindings.Select(b => b.Name).ToArray(), token);
            var values = new Dictionary<string, int>();
            foreach (var (name, type) in bindings)
            {
                var array = memory.Array(nodes[name], name, type, 4);
                if (array is null || slot >= array.Length) throw new InvalidDataException(name + " 로컬 값 미관측 (0 아님)");
                values[name] = array[slot];
            }
            return (values, trace);
        }
    }
}
