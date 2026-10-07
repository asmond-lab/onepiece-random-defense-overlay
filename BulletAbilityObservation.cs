namespace OrandOverlay;

public enum GreenBloodMarkerState { Absent, Partial, AppliedMarkers, IntrinsicSeraphim }

/// <summary>Current complete-list observation, not a cast receipt or on-target effect.</summary>
public sealed record BulletAbilityObservation(ulong EngineHandle, string Rawcode, byte Owner,
    GreenBloodMarkerState GreenBloodMarkers, bool A134, bool A13C, bool A912, bool A07N)
{
    public string Provenance => "native-stable-complete-ability-list";
    public bool? UseReceipt => null;
    public bool? ActiveEffect => null;
    public bool? ButtonEnabled => null;
    public int? A09CRemaining { get; init; }
    public bool? A13APresent { get; init; }
    public float? A09CCooldown { get; init; }
    public bool? A09CTargetEligible { get; init; }
}

public static class BulletAbilityPresentation
{
    public static string Describe(CoachFrame frame)
    {
        if (!frame.IsCurrent || frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail") return "";
        var lines = frame.CombatObservations.Where(u => u.BulletAbilities is not null &&
            u.Kind is CombatUnitKind.LocalUnit or CombatUnitKind.Bullet)
            .Select(u => u.BulletAbilities!).Where(o => o.A134 || o.A13C)
            .Select(o => "그린블러드 관련 능력: " +
                (o.GreenBloodMarkers == GreenBloodMarkerState.IntrinsicSeraphim ? "세라핌 기본 능력 확인 (변환 여부 미확인)" :
                 o.GreenBloodMarkers == GreenBloodMarkerState.AppliedMarkers ? "관련 능력 확인" : "일부 능력만 확인 · 적용 여부 미확인") +
                " · 능력은 확인했지만 실제로 사용했는지와 현재 기절·피해 효과는 확인되지 않았습니다");
        var carriers = frame.CombatObservations.Where(u => u.Rawcode == "H0C4" && u.Kind == CombatUnitKind.LocalUnit).ToArray();
        var carrier = carriers.Length == 1 ? carriers[0].BulletAbilities : null;
        var targets = frame.CombatObservations.Where(u => u.BulletAbilities?.A09CTargetEligible == true)
            .Select(u => "긴급소집 대상 후보: " +
                "내 특별함 유닛 조건을 확인했습니다. 버튼 사용 가능 여부와 사용 시점의 대상은 직접 확인하세요. 예상 결과를 보유 패에 더하지 않습니다.");
        return string.Join("\n", lines.Concat(targets).Append($"긴급소집 남은 횟수 {carrier?.A09CRemaining?.ToString() ?? "미확인"} · " +
            $"다시 사용할 때까지 {carrier?.A09CCooldown?.ToString() ?? "미확인"}초 · 그린블러드 사용 능력 " +
            (carrier?.A13APresent switch { true => "보유", false => "없음", _ => "미확인" }) + " · " +
            "게임에서 읽은 능력만으로 지금 사용할 수 있다고 판단하지 않습니다. 사용을 보류하고 버튼과 대상을 확인하세요. 항법 선택만으로 긴급소집이 3회 남았다고 보지 않습니다."));
    }
}
