using System.Collections.Immutable;
using System.Globalization;

namespace OrandOverlay;

public static class RecommendationPresentation
{
    public static PlannerEvidenceView PlannerEvidence(int round,
        AdaptivePlanningApplied? applied, bool signalsUnknown,
        string? unknownReason = null,
        StoryRewardSequenceDecision? storySequence = null,
        ManualLatches? currentManualLatches = null) =>
        PlannerEvidenceProjector.Project(
            round, applied, signalsUnknown, unknownReason, storySequence, currentManualLatches);

    public static string CarryModeLabel(GoalCarryMode mode) => mode switch
    {
        GoalCarryMode.SoloPreferred => "1상위 권장",
        GoalCarryMode.MultiAllowed => "다상위 가능",
        GoalCarryMode.MultiRequired => "다상위 필요",
        _ => "판단 보류 · 1상위 우선"
    };

    public static string ReadinessLine(CombatReadiness readiness)
    {
        var prefix = readiness.IsReady ? "55라 준비 완료" : "55라 준비 미달";
        var damage = readiness.DamageType == ReadinessDamageType.Physical
            ? $"방깎 {Format(readiness.CurrentArmorReduction)}/{Format(readiness.RequiredArmorReduction)}"
            : $"마방깎 공급원 {readiness.CurrentMagicArmorSources}/{readiness.RequiredMagicArmorSources}";
        return $"{prefix} · 스턴 {Format(readiness.CurrentStun)}/{Format(readiness.RequiredStun)}" +
               $" · 이감 {Format(readiness.CurrentSlow)}/{Format(readiness.RequiredSlow)}" +
               $" · {damage}";

        static string Format(double value) =>
            Math.Round(value, 1).ToString("0.#",
                System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string CompletionPercent(RecipeProgress progress) =>
        $"{Math.Round(progress.CompletionRatio * 100, MidpointRounding.AwayFromZero):0}%";

    public static int FlowRemainingCount(RecipeCraftStep step) => step.MissingCount;

    /// <summary>
    /// 하위패를 고르면 그 패를 짜는 데 부족한 흔함을 먼저 보여 준다.
    /// 흔함 부족이 없으면 남은 하위 재료를 그대로 둔다.
    /// </summary>
    public static IReadOnlyList<RecipeLeafProgress> BoardMissingLeaves(
        RecipeProgress progress, bool preferCommons)
    {
        var missing = progress.MissingLeaves;
        if (!preferCommons) return missing;
        var commons = missing
            .Where(leaf => leaf.Tier.Split('[', 2)[0].Trim() == "흔함")
            .ToList();
        return commons.Count > 0 ? commons : missing;
    }

    public static string AbilitySummary(CompositionUnitDetail unit)
    {
        if (unit.Abilities.Count == 0)
            return unit.UnitId.Equals("item_greenblood", StringComparison.OrdinalIgnoreCase)
                ? "특수 아이템 · 적용 수치는 대상 유닛에서 확인"
                : "표시할 능력 수치 없음";

        return string.Join(" · ", unit.Abilities.Select(ability =>
            $"{ShortAbilityName(ability.Name)} {SafeText(ability.DisplayValue)}"));
    }

    public static string RecommendationEffectLine(CompositionUnitDetail unit) =>
        AbilitySummary(unit);

    /// <summary>지금 할 일 카드 오른쪽에 줄 단위로 띄울 효과. 없으면 빈 목록.</summary>
    public static IReadOnlyList<string> NowAbilityLines(CompositionUnitDetail unit) =>
        unit.Abilities
            .Select(ability => $"{ShortAbilityName(ability.Name)} {SafeText(ability.DisplayValue)}".Trim())
            .Where(line => line.Length > 0)
            .Take(5)
            .ToList();

    /// <summary>접힌 추천 카드에 띄울 조합 명령어. 없으면 null.</summary>
    public static string? OverlayCommandLine(Recommendation item) =>
        item.CombineCommands.Count == 0
            ? null
            : "조합 명령어: " + string.Join(" / ", item.CombineCommands);

    public static string Ownership(CompositionUnitDetail unit)
    {
        var target = Math.Max(1, unit.IsRequired ? unit.RequiredCount : unit.SuggestedCount);
        return $"보유 {unit.OwnedCount}/{target}";
    }

    public static string CraftUnitName(CompositionUnitDetail unit) =>
        CraftUnitName(unit.Name, unit.Tier);

    public static string CraftUnitName(RecipeTreeNode unit) =>
        CraftUnitName(unit.Name, unit.Tier);

    public static string CraftUnitName(string name, string tier)
    {
        name = SafeText(name).Trim();
        var baseTier = SafeText(tier).Split('[', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(baseTier)) return name;
        name = RemoveTierSuffix(name, baseTier);
        return $"{name} - {baseTier}";
    }

    /// <summary>
    /// 세라핌을 만들 때 그린블러드를 줄 유닛. 보드에는 짧게, 지금 할 일에는 풀 문구.
    /// </summary>
    public static string? GreenBloodHostLine(Recommendation rec, bool compact)
    {
        if (rec.CompositionUnits.Count == 0) return null;
        if (rec.CompositionUnits[0].Tier.Split('[', 2)[0].Trim() != "세라핌") return null;
        var host = rec.RecipeTree?.Children.FirstOrDefault(child =>
            !child.UnitId.Equals("item_greenblood", StringComparison.OrdinalIgnoreCase) &&
            child.Tier.Split('[', 2)[0].Trim() is not ("아이템" or "자원"));
        if (host is null) return null;
        var tier = host.Tier.Split('[', 2)[0].Trim();
        var name = RemoveTierSuffix(SafeText(host.Name).Trim(), tier);
        if (string.IsNullOrWhiteSpace(name)) return null;
        return compact ? $"{name}에게 그블" : $"{name}에게 그린블러드";
    }

    public static string CraftIngredientLine(RecipeCraftStep step)
    {
        var select = CraftSelectUnitName(step);
        if (select is null)
        {
            var missing = CraftMissingIngredientNames(step);
            return missing is null ? "조합할 하위 유닛 없음" : "먼저 확보: " + missing;
        }
        var line = $"선택할 유닛: {select}";
        // 채팅 명령어가 있으면 그걸 우선하고, 없으면 맵 단축키. 영문 명령어는 그대로 둔다.
        if (step.CombineCommands.Count > 0)
            return line + "\n조합 명령어: " + string.Join(" / ", step.CombineCommands);
        if (step.CombineKey is { Length: > 0 } key)
            return line + $"\n유닛 조합 키: {key}";
        var companions = CraftCompanionNames(step);
        return companions is null ? line : line + "\n함께 조합: " + companions;
    }

    /// <summary>조합 시작 시 클릭해야 하는 유닛 이름. 재료가 없으면 null.</summary>
    public static string? CraftSelectUnitName(RecipeCraftStep step)
    {
        if (step.Ingredients.Count == 0) return null;
        if (step.Ingredients.Any(ingredient =>
                !IsResource(ingredient) &&
                ingredient.OwnedCount < ingredient.RequiredCount))
            return null;
        return IngredientName(step.Ingredients.OrderBy(i => i.SelectionOrder).First());
    }

    public static string? CraftMissingIngredientNames(RecipeCraftStep step)
    {
        var missing = step.Ingredients
            .Where(ingredient => !IsResource(ingredient) &&
                                 ingredient.OwnedCount < ingredient.RequiredCount)
            .OrderBy(ingredient => ingredient.SelectionOrder)
            .Select(ingredient =>
            {
                var count = ingredient.RequiredCount - ingredient.OwnedCount;
                var name = IngredientName(ingredient);
                return count > 1 ? $"{name} ×{count}" : name;
            })
            .ToList();
        return missing.Count == 0 ? null : string.Join(" / ", missing);
    }

    /// <summary>
    /// 흐름 칸에 띄울 입력. 맵 단축키(Z/X/C)가 있으면 그걸 우선하고,
    /// 초월처럼 키가 없으면 채팅 명령어를 쓴다.
    /// </summary>
    public static IReadOnlyList<string> CraftActionKeys(RecipeCraftStep step)
    {
        if (step.CombineKey is { Length: > 0 } key) return [key];
        return step.CombineCommands;
    }

    /// <summary>조합 키가 없을 때 함께 조합할 나머지 재료 목록("A / B"). 없으면 null.</summary>
    public static string? CraftCompanionNames(RecipeCraftStep step)
    {
        if (step.CombineCommands.Count > 0 || step.CombineKey is { Length: > 0 } ||
            step.Ingredients.Count <= 1) return null;
        var ordered = step.Ingredients.OrderBy(i => i.SelectionOrder).Skip(1).ToList();
        return ordered.Count == 0 ? null : string.Join(" / ", ordered.Select(IngredientName));
    }

    private static string IngredientName(RecipeCraftIngredient ingredient)
    {
        var tier = SafeText(ingredient.Tier).Split('[', 2)[0].Trim();
        // 단계별 총 부족 수량은 '남은 제작'에만 표시한다. 여기서는 실제로
        // 어떤 유닛을 선택하고 함께 조합하는지만 간결하게 안내한다.
        return RemoveTierSuffix(SafeText(ingredient.Name).Trim(), tier);
    }

    private static bool IsResource(RecipeCraftIngredient ingredient) =>
        ingredient.Tier.Split('[', 2)[0].Trim() == "자원" ||
        ingredient.UnitId.EndsWith("GOLD", StringComparison.Ordinal) ||
        ingredient.UnitId.EndsWith("LUMBER", StringComparison.Ordinal) ||
        ingredient.UnitId.EndsWith("POINT", StringComparison.Ordinal) ||
        ingredient.UnitId.EndsWith("RANDOM", StringComparison.Ordinal);

    public static string LeafStatus(RecipeLeafProgress leaf) =>
        $"보유 {leaf.OwnedCount}/{leaf.RequiredCount}장 · {leaf.MissingCount}장 부족";

    public static string SafeDescription(string value) => SafeText(value);

    public static string SafeText(string value)
    {
        var safe = KoreanLabels.ContainsLatin(value) ? KoreanLabels.RemoveLatin(value) : value;
        return safe.Replace("%", "퍼센트", StringComparison.Ordinal);
    }

    public static string ShortAbilityName(string value) => SafeText(value) switch
    {
        "이동속도 감소" => "이감",
        "발동이동속도 감소" => "발동이감",
        "방어력 감소" => "방깎",
        "발동방어력 감소" => "발동방깎",
        "중첩방어력 감소" => "중첩방깎",
        "단일방어력 감소" => "단일방깎",
        "공격력 증가" => "공증",
        "발동공격력 증가" => "발동공증",
        "공격속도 증가" => "공속",
        "마법 방어력 감소" => "마방깎",
        "마법 대미지 증가" => "마딜증폭",
        "단일마법 대미지 증가" => "단일마딜증폭",
        "폭발형 대미지 증폭" => "폭발증폭",
        "방어력 무시 대미지" => "방무뎀",
        "보스 잡기" => "보잡",
        "광폭화 잡기" => "광잡",
        "체력 재생" => "체젠",
        "마나 재생" => "마젠",
        "유닛삭제" => "유닛 삭제",
        "모든피해증가" => "모든 피해 증가",
        var name => name
    };

    private static string RemoveTierSuffix(string name, string tier)
    {
        if (!string.IsNullOrWhiteSpace(tier) &&
            name.EndsWith(" " + tier, StringComparison.OrdinalIgnoreCase))
            return name[..^(tier.Length + 1)].TrimEnd();
        return name;
    }

}

public enum PlannerEvidenceState
{
    SequenceFirstRare,
    SequenceStoryReward,
    SequenceFirstLegend,
    SequenceRareReward,
    SequenceTopNavigation,
    Waiting,
    Blocked,
    Round20Preview,
    Round21Actionable,
    Committed,
    ManualOverride,
    Round24Forced,
    Unknown
}

public enum PlannerEvidenceFieldKind
{
    Phase,
    Sequence,
    StoryStage,
    StoryReward,
    RewardValue,
    StoryDecision,
    Action,
    Blocker,
    LaneComparison,
    PhysicalRoute,
    MagicRoute,
    Package,
    FirstLegend,
    Navigation,
    NavigationOption,
    Recovery,
    Interval,
    Confidence,
    UnknownSignals,
    Restrictions,
    ForcedManual
}

public sealed record PlannerEvidenceField(
    PlannerEvidenceFieldKind Kind,
    string AutomationId,
    string Label,
    string DisplayValue,
    string MachineValue,
    string AccessibilityName,
    string AccessibilityValue,
    bool IsWarning);

public sealed record PlannerEvidenceView(
    PlannerEvidenceState State,
    ImmutableArray<PlannerEvidenceField> Fields)
{
    public bool IsManualGoal { get; init; }
    public bool IsRecommendationOnly => true;
    public bool ClaimsRuntimeSelection => false;
    public PlannerEvidenceField this[PlannerEvidenceFieldKind kind] =>
        Fields.Single(field => field.Kind == kind);
}

internal static class PlannerEvidenceProjector
{
    private const string Empty = "확인 전";

    public static PlannerEvidenceView Project(int round, AdaptivePlanningApplied? applied,
        bool signalsUnknown, string? unknownReason,
        StoryRewardSequenceDecision? storySequence = null,
        ManualLatches? currentManualLatches = null)
    {
        var manual = currentManualLatches ?? applied?.State.ManualLatches ?? ManualLatches.None;
        var state = manual.GoalOverride ? PlannerEvidenceState.ManualOverride :
            State(round, applied, signalsUnknown, storySequence);
        var trace = applied?.Trace;
        var navigation = applied?.Navigation;
        var physical = Best(trace, DamageLane.Physical);
        var magic = Best(trace, DamageLane.Magic);
        var option = navigation?.Options.FirstOrDefault(item =>
                         item.OptionId.Equals(navigation.RecommendedOptionId,
                             StringComparison.Ordinal))
                     ?? navigation?.Options.FirstOrDefault();
        var blockers = Blockers(applied);
        var unknowns = navigation?.MissingSignalIds ?? [];
        var locked = storySequence is { TopNavigationUnlocked: false };
        var fields = ImmutableArray.Create(
            Field(PlannerEvidenceFieldKind.Phase, "planner-phase", "판단 단계",
                manual.GoalOverride ? "수동 목표 유지" : StateLabel(state),
                trace?.Phase.ToString() ?? state.ToString(),
                state is PlannerEvidenceState.Blocked or PlannerEvidenceState.Unknown),
            Field(PlannerEvidenceFieldKind.Sequence, "planner-sequence", "추천 순서",
                storySequence?.StepSummary ?? Empty,
                storySequence is null
                    ? "unknown"
                    : $"{storySequence.Stage}|{storySequence.Action}"),
            Field(PlannerEvidenceFieldKind.StoryStage, "planner-story-stage", "현재 스토리",
                storySequence?.CurrentStoryLabel ?? Empty,
                storySequence?.CurrentStoryLabel ?? "unknown"),
            Field(PlannerEvidenceFieldKind.StoryReward, "planner-story-reward", "클리어 보상",
                storySequence?.ClearRewardSummary ?? Empty,
                storySequence?.ClearRewardSummary ?? "unknown"),
            Field(PlannerEvidenceFieldKind.RewardValue, "planner-reward-value", "보상 기대값",
                storySequence?.OutcomeValueSummary ?? Empty,
                storySequence?.ExpectedUsefulUnitBp.ToString(CultureInfo.InvariantCulture) ??
                "unknown"),
            Field(PlannerEvidenceFieldKind.StoryDecision, "planner-story-decision", "조합 판단",
                storySequence?.ActionSummary ?? Empty,
                storySequence?.Action.ToString() ?? "unknown"),
            Field(PlannerEvidenceFieldKind.Action, "planner-action", "지금 할 일",
                manual.GoalOverride ? "목표는 유지하고 패·보상 변화에 맞춰 조합과 보완 유닛을 계속 재계산합니다." :
                    ActionLabel(state, applied, storySequence), $"{state}|{trace?.Phase}"),
            Field(PlannerEvidenceFieldKind.Blocker, "planner-blocker", "자동 판단 조건",
                blockers.Display, blockers.Machine, blockers.IsWarning),
            Field(PlannerEvidenceFieldKind.LaneComparison, "planner-lane-comparison", "딜 경로 비교",
                LockedDisplay(locked, $"물리 {Score(physical)} · 마법 {Score(magic)}"),
                LockedMachine(locked,
                    $"physical={RawScore(physical)};magic={RawScore(magic)}")),
            Field(PlannerEvidenceFieldKind.PhysicalRoute, "planner-physical-route", "물리 경로",
                LockedDisplay(locked, RouteDisplay(physical)),
                LockedMachine(locked, RouteMachine(physical))),
            Field(PlannerEvidenceFieldKind.MagicRoute, "planner-magic-route", "마법 경로",
                LockedDisplay(locked, RouteDisplay(magic)),
                LockedMachine(locked, RouteMachine(magic))),
            Field(PlannerEvidenceFieldKind.Package, "planner-package", "목표·패키지 완성",
                LockedDisplay(locked, PackageDisplay(applied, physical, magic)),
                LockedMachine(locked, PackageMachine(applied, physical, magic))),
            Field(PlannerEvidenceFieldKind.FirstLegend, "planner-first-legend", "첫 전설 적합",
                FirstLegendDisplay(applied, storySequence),
                FirstLegendMachine(applied, storySequence)),
            Field(PlannerEvidenceFieldKind.Navigation, "planner-navigation", "항법 판단",
                LockedDisplay(locked, NavigationDisplay(navigation, option)),
                LockedMachine(locked,
                    $"regime={navigation?.Regime};posture={option?.Posture}")),
            Field(PlannerEvidenceFieldKind.NavigationOption, "planner-navigation-option", "추천 항법",
                locked ? "마지막 단계에서 공개" :
                navigation?.RecommendedOptionId is { } optionId
                    ? NavigationProfiles.Find(optionId).Name
                    : Empty,
                LockedMachine(locked, navigation?.RecommendedOptionId ?? "unknown")),
            Field(PlannerEvidenceFieldKind.Recovery, "planner-recovery", "회복 가능성",
                LockedDisplay(locked, RecoveryDisplay(option)),
                LockedMachine(locked, RecoveryMachine(option))),
            Field(PlannerEvidenceFieldKind.Interval, "planner-interval", "하한·평균·상한",
                LockedDisplay(locked, IntervalDisplay(option)),
                LockedMachine(locked, IntervalMachine(option))),
            Field(PlannerEvidenceFieldKind.Confidence, "planner-confidence", "신뢰도",
                LockedDisplay(locked, ConfidenceDisplay(trace, option)),
                LockedMachine(locked, ConfidenceMachine(trace, option)),
                !locked && (trace?.ConfidenceBp ?? 0) <
                NavigationIntervalScorer.MinimumRecommendationConfidenceBp),
            Field(PlannerEvidenceFieldKind.UnknownSignals, "planner-unknown-signals", "미확인 신호",
                UnknownDisplay(unknowns, unknownReason), UnknownMachine(unknowns, unknownReason),
                signalsUnknown || unknowns.Length > 0),
            Field(PlannerEvidenceFieldKind.Restrictions, "planner-restrictions", "제한",
                "추천만 제공 · 게임 내 선택 없음",
                "recommendation-only=true;runtime-selection=false"),
            Field(PlannerEvidenceFieldKind.ForcedManual, "planner-forced-manual", "강제·수동 상태",
                manual.GoalOverride ? "수동 목표 유지 · 자동 목표 변경 안 함" :
                    ForcedManualDisplay(trace, navigation),
                ForcedManualMachine(trace, navigation) +
                $";current-goal-manual={manual.GoalOverride};current-navigation-manual={manual.NavigationOverride}",
                state == PlannerEvidenceState.ManualOverride));
        return new PlannerEvidenceView(state, fields) { IsManualGoal = manual.GoalOverride };
    }

    private static PlannerEvidenceState State(int round, AdaptivePlanningApplied? applied,
        bool signalsUnknown, StoryRewardSequenceDecision? storySequence)
    {
        if (signalsUnknown) return PlannerEvidenceState.Unknown;
        if (storySequence is { TopNavigationUnlocked: false })
            return storySequence.Stage switch
            {
                RecommendationSequenceStage.FirstRare => PlannerEvidenceState.SequenceFirstRare,
                RecommendationSequenceStage.StoryReward => PlannerEvidenceState.SequenceStoryReward,
                RecommendationSequenceStage.FirstLegend => PlannerEvidenceState.SequenceFirstLegend,
                RecommendationSequenceStage.RareReward => PlannerEvidenceState.SequenceRareReward,
                _ => PlannerEvidenceState.SequenceTopNavigation
            };
        if (applied is null) return PlannerEvidenceState.Waiting;
        if (applied.State.ManualLatches.GoalOverride ||
            applied.State.ManualLatches.NavigationOverride ||
            applied.Navigation.State == NavigationRecommendationState.ManualOverride)
            return PlannerEvidenceState.ManualOverride;
        if (round >= 24 && (applied.Navigation.State == NavigationRecommendationState.SourceExpectedForced ||
                            applied.Trace.SourceDefinedForcedExpectationId is not null))
            return PlannerEvidenceState.Round24Forced;
        if (round == 20) return PlannerEvidenceState.Round20Preview;
        if (applied.Navigation.State == NavigationRecommendationState.Locked)
            return PlannerEvidenceState.Committed;
        if (round is >= 21 and <= 23 &&
            applied.Navigation.State == NavigationRecommendationState.Actionable)
            return PlannerEvidenceState.Round21Actionable;
        if (applied.Blockers.Length > 0 || applied.Navigation.Blockers.Length > 0 ||
            applied.Navigation.State == NavigationRecommendationState.NoSafeRecommendation)
            return PlannerEvidenceState.Blocked;
        return applied.State.Phase == PlannerPhase.Committed
            ? PlannerEvidenceState.Committed
            : PlannerEvidenceState.Waiting;
    }

    private static AdaptiveRouteComponent? Best(AdaptiveDecisionEvent? trace, DamageLane lane) =>
        trace?.RouteComponents.Where(item => item.Lane == lane)
            .OrderByDescending(item => item.RouteScoreBp).FirstOrDefault();

    private static PlannerEvidenceField Field(PlannerEvidenceFieldKind kind, string id,
        string label, string display, string machine, bool warning = false) =>
        new(kind, id, label, display, machine, label, display, warning);

    private static string StateLabel(PlannerEvidenceState state) => state switch
    {
        PlannerEvidenceState.SequenceFirstRare => "1단계 · 첫 희귀함",
        PlannerEvidenceState.SequenceStoryReward => "2단계 · 스토리 보상",
        PlannerEvidenceState.SequenceFirstLegend => "3단계 · 첫 전설",
        PlannerEvidenceState.SequenceRareReward => "4단계 · 희귀 보상",
        PlannerEvidenceState.SequenceTopNavigation => "5단계 · 상위·항법 대기",
        PlannerEvidenceState.Waiting => "판단 입력 대기",
        PlannerEvidenceState.Blocked => "판단 보류",
        PlannerEvidenceState.Round20Preview => "20라운드 미리보기",
        PlannerEvidenceState.Round21Actionable => "21~23라운드 추천 가능",
        PlannerEvidenceState.Committed => "추천 고정",
        PlannerEvidenceState.ManualOverride => "수동 설정 유지",
        PlannerEvidenceState.Round24Forced => "24라운드 원본 규칙 기대값",
        _ => "입력 확인 필요"
    };

    private static string ActionLabel(PlannerEvidenceState state, AdaptivePlanningApplied? applied,
        StoryRewardSequenceDecision? storySequence) =>
        storySequence is { TopNavigationUnlocked: false }
            ? storySequence.ActionSummary
            :
        state switch
        {
            PlannerEvidenceState.Waiting => "게임 신호를 확인하는 중입니다.",
            PlannerEvidenceState.Blocked => "차단 사유를 확인하고 기존 추천을 유지합니다.",
            PlannerEvidenceState.Round20Preview => "두 딜 경로와 항법 후보를 비교합니다.",
            PlannerEvidenceState.Round21Actionable => "표시된 항법을 참고해 직접 선택하세요.",
            PlannerEvidenceState.Committed => "고정된 빌드와 항법 추천을 유지합니다.",
            PlannerEvidenceState.ManualOverride => "사용자가 고른 오버레이 설정을 유지합니다.",
            PlannerEvidenceState.Round24Forced => "맵 원본 규칙의 24라운드 기대값을 안내합니다.",
            _ => applied?.State.Phase == PlannerPhase.ChooseLegend
                ? "표시된 첫 전설 후보를 확인하세요."
                : "미확인 신호가 안정될 때까지 마지막 추천을 유지합니다."
        };

    private static (string Display, string Machine, bool IsWarning) Blockers(
        AdaptivePlanningApplied? applied)
    {
        var build = applied?.Blockers.Select(item => item.ToString()) ?? [];
        var navigation = applied?.Navigation.Blockers.Select(item => item.ToString()) ?? [];
        var values = build.Concat(navigation).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 0
            ? ("없음", "none", false)
            : (string.Join("\n", values.Select(PlannerInputExplanation.Blocker)),
                string.Join(',', values), true);
    }

    private static string Score(AdaptiveRouteComponent? route) =>
        route is null ? Empty : Bp(route.RouteScoreBp);
    private static string RawScore(AdaptiveRouteComponent? route) =>
        route?.RouteScoreBp.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    private static string RouteDisplay(AdaptiveRouteComponent? route) => route is null
        ? Empty
        : $"{(route.Lane == DamageLane.Physical ? "물리" : "마법")} 1순위 · {Bp(route.RouteScoreBp)}";
    private static string RouteMachine(AdaptiveRouteComponent? route) => route is null
        ? "unknown"
        : $"{route.CandidateId}|{route.RouteScoreBp}|{route.GoalProgressBp}|{route.PackageProgressBp}";

    private static AdaptiveRouteComponent? SelectedRoute(AdaptivePlanningApplied? applied,
        AdaptiveRouteComponent? physical, AdaptiveRouteComponent? magic) =>
        applied?.State.RouteLock?.Lane == DamageLane.Magic ? magic : physical ?? magic;

    private static string PackageDisplay(AdaptivePlanningApplied? applied,
        AdaptiveRouteComponent? physical, AdaptiveRouteComponent? magic)
    {
        var route = SelectedRoute(applied, physical, magic);
        return route is null ? Empty
            : $"선택 목표 {Bp(route.GoalProgressBp)} · 지원 패키지 {Bp(route.PackageProgressBp)}";
    }

    private static string PackageMachine(AdaptivePlanningApplied? applied,
        AdaptiveRouteComponent? physical, AdaptiveRouteComponent? magic)
    {
        var route = SelectedRoute(applied, physical, magic);
        return route is null ? "unknown"
            : $"{applied?.State.RouteLock?.PackageId}|goal={route.GoalProgressBp}|package={route.PackageProgressBp}";
    }

    private static string FirstLegendDisplay(AdaptivePlanningApplied? applied,
        StoryRewardSequenceDecision? storySequence) =>
        storySequence?.RecommendedLegendName is { Length: > 0 } name
            ? $"{name} · {storySequence.ActionSummary}"
            :
        applied?.State.LockedFirstLegendId is not null
            ? "첫 전설 확정 · 선택 경로에 반영"
            : applied?.State.PossibleFirstLegendIds.Length > 0
                ? $"후보 {applied.State.PossibleFirstLegendIds.Length}개"
                : Empty;
    private static string FirstLegendMachine(AdaptivePlanningApplied? applied,
        StoryRewardSequenceDecision? storySequence) =>
        storySequence?.RecommendedLegendId ??
        applied?.State.LockedFirstLegendId ??
        (applied?.State.PossibleFirstLegendIds.Length > 0
            ? string.Join(',', applied.State.PossibleFirstLegendIds) : "unknown");

    private static string NavigationDisplay(NavigationIntervalScoringResult? navigation,
        NavigationIntervalOptionScore? option) => navigation is null
        ? Empty
        : $"{RegimeLabel(navigation.Regime)} · {PostureLabel(option?.Posture)}";
    private static string RegimeLabel(NavigationScoringRegime regime) => regime switch
    {
        NavigationScoringRegime.SecureCore => "핵심 완성 우선",
        NavigationScoringRegime.GuaranteedRecovery => "확정 회복 우선",
        NavigationScoringRegime.DesperationRecovery => "회복 가능성 우선",
        _ => "판단 대기"
    };
    private static string PostureLabel(NavigationRiskPosture? posture) => posture switch
    {
        NavigationRiskPosture.FloorDefense => "하한 방어",
        NavigationRiskPosture.HighCeiling => "상한 추구",
        NavigationRiskPosture.Balanced => "균형",
        _ => "성향 확인 전"
    };

    private static string RecoveryDisplay(NavigationIntervalOptionScore? option)
    {
        var probability = Recovery(option);
        return probability is null ? Empty : RatioPercent(probability.Value);
    }
    private static string RecoveryMachine(NavigationIntervalOptionScore? option) =>
        Recovery(option)?.ToString() ?? "unknown";
    private static Rational? Recovery(NavigationIntervalOptionScore? option) =>
        option?.Scenarios.Length > 0
            ? option.Scenarios.Select(item => item.RecoveryProbability)
                .OrderBy(value => (double)value.Numerator / (double)value.Denominator).First()
            : null;

    private static string IntervalDisplay(NavigationIntervalOptionScore? option)
    {
        if (option is null) return Empty;
        var mean = (option.Value.Lower + option.Value.Upper) / 2;
        return $"{Bp(option.Floor.Lower)} · {Bp(mean)} · {Bp(option.Upper.Upper)}";
    }
    private static string IntervalMachine(NavigationIntervalOptionScore? option)
    {
        if (option is null) return "unknown";
        var mean = (option.Value.Lower + option.Value.Upper) / 2;
        return $"floor={option.Floor.Lower};mean={mean};ceiling={option.Upper.Upper}";
    }

    private static string ConfidenceDisplay(AdaptiveDecisionEvent? trace,
        NavigationIntervalOptionScore? option) => trace is null && option is null
        ? Empty
        : $"판단 {Bp(trace?.ConfidenceBp ?? 0)} · 항법 {Bp(option?.ConfidenceBp ?? 0)}";
    private static string ConfidenceMachine(AdaptiveDecisionEvent? trace,
        NavigationIntervalOptionScore? option) =>
        $"decision={trace?.ConfidenceBp.ToString(CultureInfo.InvariantCulture) ?? "unknown"};" +
        $"navigation={option?.ConfidenceBp.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";

    private static string UnknownDisplay(ImmutableArray<string> unknowns, string? reason) =>
        string.Join("\n", unknowns.Select(PlannerInputExplanation.Signal)
            .Append(reason).Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } detail
            ? detail : "없음";
    private static string UnknownMachine(ImmutableArray<string> unknowns, string? reason) =>
        string.Join('|', unknowns.Append(reason).Where(value => !string.IsNullOrWhiteSpace(value))!);

    private static string ForcedManualDisplay(AdaptiveDecisionEvent? trace,
        NavigationIntervalScoringResult? navigation)
    {
        if (trace?.ManualLatches.GoalOverride == true ||
            trace?.ManualLatches.NavigationOverride == true ||
            navigation?.State == NavigationRecommendationState.ManualOverride)
            return "수동 설정 우선";
        return trace?.SourceDefinedForcedExpectationId is { } forced
            ? $"원본 규칙 기대값 · {NavigationProfiles.Find(forced).Name}"
            : "자동 추천";
    }
    private static string ForcedManualMachine(AdaptiveDecisionEvent? trace,
        NavigationIntervalScoringResult? navigation) =>
        $"goal-manual={trace?.ManualLatches.GoalOverride == true};" +
        $"navigation-manual={trace?.ManualLatches.NavigationOverride == true};" +
        $"state={navigation?.State};source-forced={trace?.SourceDefinedForcedExpectationId ?? "none"}";

    private static string LockedDisplay(bool locked, string value) =>
        locked ? "마지막 단계에서 공개" : value;

    private static string LockedMachine(bool locked, string value) =>
        locked ? "locked-by-sequence" : value;

    private static string Bp(int value) => (value / 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string RatioPercent(Rational value) =>
        (100d * (double)value.Numerator / (double)value.Denominator)
        .ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
