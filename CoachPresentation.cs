namespace OrandOverlay;

// Display-only projection. Never changes a decision, observation, or execution permission.
public sealed record CoachPresentation(string Title, string Controls, string Status, string NavigationAlert, bool IsStart)
{
    public bool ShowEssentialReason { get; init; }
    public bool ShowConfirmation { get; init; }
    public int? RemainingGuideCrafts { get; init; }
    public string? GuideCraftTargetName { get; init; }
    public ObservedCraftProgress? CraftProgress { get; init; }
    public static string ActionTitle(CoachDecision decision, UnitDefinition? unit) => unit is null
        ? decision.Title
        : decision.Title.Replace(RecommendationPresentation.CoachUnitName(unit.Name, unit.Tier),
            unit.Name, StringComparison.Ordinal);
    // Keep diagnostic reasons on CoachDecision; only project the text shown by the coach.
    public static string DisplayReason(CoachDecision decision, CoachFrame frame)
    {
        if (decision.Id.StartsWith("recipe-conditions:", StringComparison.Ordinal))
        {
            var reason = decision.Reason;
            if (reason.Contains("실제 영웅", StringComparison.Ordinal)) return reason;
            if (reason.Contains("특성강화", StringComparison.Ordinal) && reason.Contains("토큰", StringComparison.Ordinal))
                return "조합을 잠시 미뤄 주세요. 게임에서 특성강화, 필요한 아이템과 목재를 확인해 주세요.";
            if (reason.Contains("특성강화", StringComparison.Ordinal))
                return "조합을 잠시 미뤄 주세요. 게임에서 특성강화를 확인해 주세요.";
            if (reason.Contains("토큰", StringComparison.Ordinal))
                return "조합을 잠시 미뤄 주세요. 게임에서 필요한 아이템을 확인해 주세요.";
            if (reason.Contains("목재", StringComparison.Ordinal))
                return "조합을 잠시 미뤄 주세요. 게임에서 목재를 확인해 주세요.";
            return "조합 조건을 아직 확인하지 못했습니다. 게임에서 필요한 조건을 확인하기 전에는 재료를 사용하지 마세요.";
        }
        if (frame.Recommendations.FirstOrDefault()?.Warnings.Contains(decision.Reason, StringComparer.Ordinal) != true)
            return decision.Reason;
        var warning = decision.Reason;
        if (warning.Contains("7강", StringComparison.Ordinal) && warning.Contains("20~28", StringComparison.Ordinal))
            return "7강에서 멈추고 리롤을 포함해 목재는 20~28개까지만 사용하세요.";
        if (warning.Contains("대체 기물", StringComparison.Ordinal))
            return "조합에 쓸 전투 유닛을 대신할 유닛을 먼저 확보해 주세요.";
        if (warning.Contains("하위 패", StringComparison.Ordinal))
            return "목표 재료를 사용하게 됩니다. 부족한 하위 유닛 수를 확인하고 더 모아 주세요.";
        if (warning.Contains("목재·골드", StringComparison.Ordinal))
            return "유닛 재료와 별도로 게임에서 목재, 골드, 특수 포인트를 확인한 뒤 조합해 주세요.";
        if (warning.Contains("홀딩", StringComparison.Ordinal))
            return "적을 붙잡는 유닛이 충분한지 확인하고, 남는 유닛으로 공격을 보강해 주세요.";
        return "추천에 필요한 조건을 확인해 주세요. 확인 전에는 재료를 사용하지 마세요.";
    }

    public static CoachPresentation Create(CoachDecision decision, CoachFrame frame)
    {
        var start = decision.Id == "start";
        // Provenance is exact membership in the current lead, never warning-word guessing.
        var recommendationWarning = !string.IsNullOrEmpty(decision.Reason) &&
            frame.Recommendations.FirstOrDefault()?.Warnings.Contains(decision.Reason, StringComparer.Ordinal) == true;
        var guideRecommendation = frame.IsCurrent && frame.Mode == PlayMode.Guide && frame.GuidePlan?.TargetUnitId is { } guideTarget
            ? frame.Recommendations.FirstOrDefault(item => item.Route.GoalUnitId == guideTarget) : null;
        var guideRoot = guideRecommendation?.RecipeTree;
        return new(start ? "게임 연결 대기" : decision.Title,
            start ? "게임을 시작하면 패를 인식합니다." : decision.Controls,
            start ? "" : frame.Round > 0 ? $"{frame.DifficultyLabel} · {frame.Round}라" :
                $"{frame.DifficultyLabel} · 라운드 미확인",
            start ? "" : Navigation(frame), start)
        {
            GuideCraftTargetName = guideRoot?.Name,
            CraftProgress = decision.CraftProgress,
            RemainingGuideCrafts = guideRoot is null ? null :
                frame.Inventory.GetValueOrDefault(guideRoot.UnitId) > 0 ? 0 :
                checked((int)(Math.Max(0, guideRoot.RequiredCount - guideRoot.OwnedCount) +
                    guideRecommendation!.RemainingCraftSteps.Where(step => step.UnitId != guideRoot.UnitId)
                        .Sum(step => Math.Max(0, step.MissingCount)))),
            ShowEssentialReason = !start && (recommendationWarning || decision.IsUrgent || decision.CraftDeferredForReward ||
                decision.Kind is CoachActionKind.Reward or CoachActionKind.Economy or CoachActionKind.Upgrade or
                    CoachActionKind.Waiting or CoachActionKind.Recognition || !string.IsNullOrEmpty(decision.Constraint)),
            ShowConfirmation = !start && (decision.RequiresUserConfirmation || decision.CraftDeferredForReward ||
                decision.Kind is CoachActionKind.Reward or CoachActionKind.Waiting or CoachActionKind.Recognition)
        };
    }

    private static string Navigation(CoachFrame frame)
    {
        if (!frame.IsCurrent) return "현재 패를 다시 확인하고 있습니다. 확인될 때까지 재료를 사용하지 마세요.";
        var native = frame.NativeNavigation;
        if (native.Status is NativeNavigationStatus.Conflict or NativeNavigationStatus.Unselected)
            return NativeNavigationPresentation.Describe(native, frame.ConfirmedNavigation);
        if (native.Status == NativeNavigationStatus.Selected && native.OptionId is { } id)
        {
            var mismatch = frame.Mode == PlayMode.Guide && frame.GuideNumber == 1 && id != BulletGuidePolicy.NavigationId;
            return $"항법 · {NavigationProfiles.Find(id).Name}" +
                (mismatch ? " · 선택한 항법이 공략 권장 항법과 다릅니다." : " · 게임에서 확인");
        }
        return NativeNavigationPresentation.Describe(native, frame.ConfirmedNavigation);
    }
}
