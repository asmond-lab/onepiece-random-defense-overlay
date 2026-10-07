namespace OrandOverlay;

public enum NasjuroApplicability { NotApplicable, Unknown, Applicable }
public sealed record NasjuroWispAdvice(NasjuroApplicability Applicability,
    bool SuppressGenericRayleigh, string Guidance, string UnknownSignals)
{
    // The current wood-choice menu is not identified. Never synthesize a target or key.
    public CoachDecision? ExactChoice => null;
}

public static class NasjuroWispAdvicePolicy
{
    public static bool Eligible(CoachFrame frame) => frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 &&
        frame.IsCurrent && !frame.Paused && frame.HasKnownDifficulty && frame.Outcome is not ("clear" or "fail") &&
        frame.GuidePlan is not null;

    public static NasjuroWispAdvice Evaluate(CoachFrame frame, ShipReservations? allocation = null)
    {
        allocation ??= frame.ShipReservations;
        if (allocation is not null && !ShipReservationPolicy.Matches(allocation, frame)) allocation = null;
        var selection = frame.PlanningGorosei;
        var fresh = selection.IsKnown;
        var hasBody = frame.Inventory.GetValueOrDefault("rawcode:3A0h") > 0;
        if (!Eligible(frame) || fresh && selection.Mode != GoroseiMode.Nasjuro || !hasBody)
            return new(NasjuroApplicability.NotApplicable, false, "", "");
        var actual = fresh && frame.MatchGeneration > 0 && frame.RecognitionRevision > 0 &&
            frame.CombatObservations.Any(unit => unit.Rawcode == "h0A3" &&
            unit.Kind == CombatUnitKind.LocalUnit && unit.Owner < 6 && unit.Life is > 0 and var life && float.IsFinite(life) && !unit.MirrorCopy);
        var guidance = !actual
            ? "나스쥬로·미호크 세라핌 현재 관측 미확인: 배와 초월위습을 보호하고 현재 오로성·생존·배 수요부터 확인합니다. 저장 설정·미호크 히든·그린블러드 계획은 실제 세라핌 근거가 아닙니다."
            : allocation?.IsKnown != true
                ? "나스쥬로 + 실제 미호크 세라핌이지만 배 수요 미확인: 선택을 보류하고 배·초월위습을 보호합니다. 목재를 먼저 고르도록 권하지 않습니다."
                : allocation.Ships.Values.Any(ship => ship.Missing > 0)
                    ? "나스쥬로 + 실제 미호크 세라핌: 다른 목표의 추가 배 부족을 먼저 해결하세요. 예약된 배를 보존하고 부족한 배 종류·개수에 맞는 확보 경로를 확인합니다. 일반 레일리+해적선 선택은 모든 배 부족을 해결하지 않으며 자동 강제하지 않습니다."
                    : "나스쥬로 + 실제 미호크 세라핌: 추가 배 부족 없음. 이미 확보된 예약 배는 먼저 유지하고 목재 확보 쪽을 더 자주 고려합니다. 목박:레박 8:2는 상황별 대략적 선호이며 고정 순서·횟수·확률이 아닙니다.";
        return new(actual && allocation?.IsKnown == true ? NasjuroApplicability.Applicable : NasjuroApplicability.Unknown, true,
            guidance + (allocation is null ? "" : "\n" + allocation.Summary),
            "목재 확보 메뉴 미확인 · " + (allocation?.IsKnown == true ? "조합에 쓸 배를 먼저 남기세요." : "배 수요 미확인.") +
            " 어떤 상자나 단축키를 쓸지는 안내하지 않습니다. 받지 않은 목재 4개를 보유량에 더하지 않습니다.");
    }

    public static CoachDecision? Decide(CoachFrame frame, DataCatalog catalog)
    {
        if (frame.RewardWisps.GetValueOrDefault("e01A") <= 0) return null;
        var allocation = frame.ShipReservations is { } shared && ShipReservationPolicy.Matches(shared, frame)
            ? shared : ShipReservationPolicy.Evaluate(frame, catalog);
        frame = frame with { ShipReservations = allocation };
        var advice = Evaluate(frame, allocation);
        if (!advice.SuppressGenericRayleigh) return null;
        return new(CoachActionKind.Maintain, "guide1:nasjuro-wisp:" + advice.Applicability,
            "조합에 쓸 배 유지 · 초월위습 사용 방향 확인", "현재 패와 배를 보존하세요. 미확인 목재 메뉴를 자동 지시하지 않습니다.",
            advice.Guidance, "현재 오로성·실제 세라핌 생존·배 수요를 다음 정상 인식으로 재확인합니다.",
            BulletGuideAdvice.Stage(frame.GuidePlan!))
        { OperationGuide = BulletGuideAdvice.Operation(frame), UnknownSignals = advice.UnknownSignals,
            PreservedMaterials = allocation.Summary, PreservedMaterialCounts = allocation.PreservedCounts,
            GoalLabel = "공략 1 · 더글라스 불릿" };
    }
}
