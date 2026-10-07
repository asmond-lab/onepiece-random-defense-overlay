using System.Collections.Immutable;
namespace OrandOverlay;

public enum GoroseiMarkerStatus { Unknown, SelectedIdentity, Conflict }
public sealed record GoroseiMarkerCandidate(ulong Pointer, string Rawcode, byte Owner, ulong? EngineHandle,
    float? Life, bool IdentityVerified, bool AliveVerified);
public sealed record GoroseiMarkerSnapshot(GoroseiMarkerStatus Status, GoroseiMode Mode, string Detail,
    ImmutableArray<GoroseiMarkerCandidate> Candidates)
{
    public static GoroseiMarkerSnapshot Unknown { get; } = new(GoroseiMarkerStatus.Unknown, GoroseiMode.None,
        "오로성 현재 identity 미확인 · 효과 활성 미확인", []);
    public bool EffectsActiveVerified => false; // Map marker exists before actual appearance; no effect-phase producer yet.
}
internal sealed class WarcraftGoroseiReader(Func<ulong, int, byte[]> read, ulong moduleBase, int moduleSize)
{
    internal GoroseiMarkerSnapshot Read(string version, string mapHash,
        IEnumerable<(ulong Address, uint Rawcode)> candidates, CancellationToken token = default)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 || moduleSize < 0x288)
            return GoroseiMarkerSnapshot.Unknown;
        var input = candidates.Distinct().ToArray();
        var output = ImmutableArray.CreateBuilder<GoroseiMarkerCandidate>();
        foreach (var (unit, rawcode) in input)
        {
            token.ThrowIfCancellationRequested();
            GoroseiMarkerCandidate? accepted = null;
            try
            {
                var first = Snapshot(unit, rawcode); var second = Snapshot(unit, rawcode);
                if (first.Candidate == second.Candidate && first.Trace.SequenceEqual(second.Trace)) accepted = second.Candidate;
            }
            catch (Exception e) when (e is InvalidDataException or System.ComponentModel.Win32Exception or OverflowException) { }
            output.Add(accepted ?? new(unit, RouteQuestMemory.Rawcode(unchecked((int)rawcode)), 7, null, null, false, false));
        }
        var all = output.OrderBy(c => c.Pointer).ToImmutableArray();
        if (all.Select(c => c.Rawcode).Distinct(StringComparer.Ordinal).Count() > 1)
            return new(GoroseiMarkerStatus.Conflict, GoroseiMode.None,
                "오로성 복수 marker 충돌 · 열거 순서로 선택하지 않음 · 효과 미적용", all);
        return all.Length == 1 && all[0] is { IdentityVerified: true, AliveVerified: true }
            ? new(GoroseiMarkerStatus.SelectedIdentity, GoroseiMemoryDetector.FromRawcode(input[0].Rawcode),
                "오로성 선택 identity 확인 · 등장 전 marker일 수 있음 · 효과 활성 미확인", all)
            : GoroseiMarkerSnapshot.Unknown with { Candidates = all };

        (GoroseiMarkerCandidate Candidate, List<(ulong, string)> Trace) Snapshot(ulong unit, uint rawcode)
        {
            var trace = new List<(ulong, string)>();
            byte[] Read(ulong address, int length)
            {
                token.ThrowIfCancellationRequested();
                var bytes = read(address, length); trace.Add((address, Convert.ToHexString(bytes))); return bytes;
            }
            var memory = new RouteQuestMemory(Read);
            ulong U64(ulong address) => BitConverter.ToUInt64(memory.Bytes(address, 8));
            uint U32(ulong address) => BitConverter.ToUInt32(memory.Bytes(address, 4));
            var handles = new WarcraftHandleResolver(Read, moduleBase);
            if (GoroseiMemoryDetector.FromRawcode(rawcode) == GoroseiMode.None ||
                !ReadOnlyProcessMemory.IsPlausibleUserAddress(unit) || U32(checked(unit + 0x178)) != rawcode ||
                memory.Bytes(checked(unit + 0x1c0), 1)[0] != 7) throw new InvalidDataException("marker identity 불일치");
            var vtable = U64(unit);
            if (vtable < moduleBase || checked(vtable + 0x288) > checked(moduleBase + (ulong)moduleSize) ||
                U64(checked(vtable + 0x178)) != checked(moduleBase + 0x1163ad0) ||
                U64(checked(vtable + 0x278)) != checked(moduleBase + 0x1163a00) ||
                U64(checked(vtable + 0x280)) != checked(moduleBase + 0x1163a20)) throw new InvalidDataException("CUnit 검증 실패");
            var self = U64(checked(unit + 0x18));
            if (handles.Entry(self) is not { } agent || U32(checked(agent + 0x18)) != 0x2b61676c ||
                U64(checked(agent + 0x30)) != 0 || U64(checked(agent + 0x90)) != unit || handles.Object(self) != unit)
                throw new InvalidDataException("self handle 검증 실패");
            var lifeHandle = U64(checked(unit + 0x258));
            var life = new WarcraftStateReader(Read, moduleBase).Regeneration(lifeHandle);
            if (U64(checked(unit + 0x258)) != lifeHandle || U64(checked(unit + 0x18)) != self ||
                handles.Object(self) != unit || U32(checked(unit + 0x178)) != rawcode ||
                memory.Bytes(checked(unit + 0x1c0), 1)[0] != 7) throw new InvalidDataException("marker 변경 중");
            return (new(unit, RouteQuestMemory.Rawcode(unchecked((int)rawcode)), 7, self,
                life?.Current, true, life is { Current: > 0, Maximum: > 0 }), trace);
        }
    }
}
