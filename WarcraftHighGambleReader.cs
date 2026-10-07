namespace OrandOverlay;

public sealed record HighGambleObservation(bool IsVerified, bool Active, int? Failures, string Detail)
{
    public static HighGambleObservation Unknown { get; } = new(false, false, null, "OE 실패 누적 미확인");
}

internal sealed class WarcraftHighGambleReader
{
    internal HighGambleObservation Read(Func<ulong, int, byte[]> read, string version, string mapHash,
        byte? slot, ulong? anchor, RouteQuestSnapshot quests, CancellationToken token = default)
    {
        if (version != "2.0.4.23745" || mapHash != RouteQuestCatalog.MapScriptSha256 ||
            slot is null or > 3 || anchor is null || !quests.IsVerified) return HighGambleObservation.Unknown;
        try
        {
            var values = StableMapGlobals.Read(read, anchor.Value, [("OE", 9), ("cr", 13)], slot.Value, token);
            var failures = values["OE"]; var active = values["cr"];
            if (failures < 0 || active is < 0 or > 1 ||
                (quests.Status("Q006") == RouteQuestStatus.Active) != (active == 1))
                return HighGambleObservation.Unknown with { Detail = "Q006 배정·활성·OE 불일치 · 재조회 필요" };
            return new(true, active == 1, failures, active == 1 && failures >= 4
                ? "Q006 완료 전이 재조회 · 추가 도박 지시 없음" : "native OE 누적 실패 확인 · 시도 횟수와 별개");
        }
        catch (Exception e) when (e is InvalidDataException or System.ComponentModel.Win32Exception or OverflowException)
        { return HighGambleObservation.Unknown with { Detail = "OE 실패 누적 미확인 · " + e.Message }; }
    }
}
