using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrandOverlay;

public sealed record BulletStrategyProfile(
    int SchemaVersion,
    string GoalUnitId,
    string GoalRawcode,
    string ReferenceSha256,
    ImmutableArray<string> FirstLegendPriorityUnitIds,
    ImmutableArray<string> FlyingCapableLegendUnitIds,
    ImmutableArray<string> BossKillUnitIds,
    ImmutableArray<string> StunUnitIds,
    int FlyingTarget,
    int FlyingStoryBoundary,
    int FlyingRoundBoundary,
    int BossKillTarget,
    double ExternalSlowTarget,
    double BulletSlowContribution,
    double AuraArmorReductionTarget,
    double BulletArmorReductionContribution,
    double GreenBloodStunContribution,
    int Round50CraftRound,
    ImmutableArray<string> EnhancementPriority,
    string DefaultNavigationId,
    string FallbackNavigationId,
    bool RecommendationOnly,
    ImmutableArray<string> RejectedEarlyDeadEnds,
    ImmutableArray<string> Constraints)
{
    public ImmutableArray<string> AllCapabilityUnitIds =>
        FirstLegendPriorityUnitIds
            .Concat(FlyingCapableLegendUnitIds)
            .Concat(BossKillUnitIds)
            .Concat(StunUnitIds)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
}

public static class BulletStrategyProfileLoader
{
    private const string ProfileFileName = "bullet-strategy-2314.json";
    private const string CatalogFileName = "tmo-unit-catalog.json";
    private const string ExpectedGoalId = "rawcode:180h";
    private const string ExpectedGoalRawcode = "180h";
    private const string ExpectedReferenceSha256 =
        "ef69ae6ed92ab0c44085604878c7fdb3612e6c23adf6a76174946d6188596412";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public static BulletStrategyProfile LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            var path = Path.Combine(directory, ProfileFileName);
            var profile = JsonSerializer.Deserialize<BulletStrategyProfile>(
                File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidDataException($"{ProfileFileName} is empty.");
            Validate(profile, Path.Combine(directory, CatalogFileName));
            return profile;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException($"Invalid {ProfileFileName}.", exception);
        }
    }

    private static void Validate(BulletStrategyProfile profile, string catalogPath)
    {
        if (profile.SchemaVersion != 1 || profile.GoalUnitId != ExpectedGoalId ||
            profile.GoalRawcode != ExpectedGoalRawcode ||
            profile.ReferenceSha256 != ExpectedReferenceSha256 || !profile.RecommendationOnly)
            throw new InvalidDataException("Bullet profile provenance or identity is invalid.");
        if (profile.FlyingTarget != 2 || profile.BossKillTarget != 2 ||
            profile.FlyingStoryBoundary != 12 || profile.FlyingRoundBoundary != 50 ||
            profile.ExternalSlowTarget != 82 || profile.BulletSlowContribution != 20 ||
            profile.AuraArmorReductionTarget != 100 ||
            profile.BulletArmorReductionContribution <= 0 ||
            profile.GreenBloodStunContribution != 0.3 || profile.Round50CraftRound != 50)
            throw new InvalidDataException("Bullet profile target sequence is invalid.");
        if (!profile.EnhancementPriority.SequenceEqual(
                ["armor-reduction", "attack-speed", "attack-power"], StringComparer.Ordinal) ||
            profile.DefaultNavigationId != "PathOfKings.BountyHunter" ||
            profile.FallbackNavigationId != "EmergencyCall")
            throw new InvalidDataException("Bullet enhancement or navigation policy is invalid.");
        if (profile.AllCapabilityUnitIds.Any(id =>
                !id.StartsWith("rawcode:", StringComparison.Ordinal) || id.Length <= 8))
            throw new InvalidDataException("Bullet capability IDs must be canonical rawcode IDs.");

        using var catalog = JsonDocument.Parse(File.ReadAllBytes(catalogPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
        var catalogRawcodes = catalog.RootElement.GetProperty("units").EnumerateArray()
            .Select(unit => unit.GetProperty("rawcode").GetString())
            .Where(rawcode => rawcode is not null)
            .ToHashSet(StringComparer.Ordinal);
        var missing = profile.AllCapabilityUnitIds
            .Select(id => id[8..])
            .Where(rawcode => !catalogRawcodes.Contains(rawcode))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("Unknown Bullet capability rawcodes: " +
                                           string.Join(",", missing));
    }
}

public enum BulletOperatingBoardState
{
    EarlyFoundation,
    AirMobility,
    BossKill,
    ControlArmor,
    Ready,
    Round50
}

public enum BulletOperatingBoardFieldKind
{
    Flying,
    BossKill,
    Slow,
    ArmorReduction,
    Stun,
    Craft,
    Enhancement,
    Navigation
}

public sealed record BulletOperatingBoardField(
    BulletOperatingBoardFieldKind Kind,
    string AutomationId,
    string Label,
    string DisplayValue,
    string AccessibilityName,
    string AccessibilityValue,
    bool IsWarning);

public sealed record BulletOperatingBoard(
    BulletOperatingBoardState State,
    int Round,
    string Phase,
    string Confidence,
    string Action,
    string Objective,
    ImmutableArray<string> Routes,
    string Focus,
    string Gate,
    string? CheckNeeded,
    string? Recovery,
    string Blocker,
    ImmutableArray<BulletOperatingBoardField> Fields)
{
    public bool IsRecommendationOnly => true;
    public bool ClaimsRuntimeNavigationSelection => false;
    public BulletOperatingBoardField this[BulletOperatingBoardFieldKind kind] =>
        Fields.Single(field => field.Kind == kind);
}

public sealed record BulletOperatingBoardInput(
    int Round,
    string? SelectedGoalId,
    string? CommittedGoalId,
    bool? FirstLegendFoundationKnown,
    int? FlyingCapableLegendCount,
    int? BossKillUnitCount,
    double? ExternalSlow,
    double? AuraArmorReduction,
    double? Stun,
    double StunTarget,
    bool? GreenBloodKnown,
    bool? BulletCrafted,
    string? EnhancementStatus,
    string UnknownReason)
{
    public static BulletOperatingBoardInput Unknown(int round, string? selectedGoalId,
        string? committedGoalId, string reason) => new(round, selectedGoalId, committedGoalId,
        null, null, null, null, null, null, 1.4, null, null, null, reason);
}

public static class BulletOperatingBoardPolicy
{
    public static BulletOperatingBoard? Evaluate(BulletStrategyProfile profile,
        BulletOperatingBoardInput input)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(input);
        if (!IsBullet(profile, input.SelectedGoalId) &&
            !IsBullet(profile, input.CommittedGoalId)) return null;

        var unknownReason = string.IsNullOrWhiteSpace(input.UnknownReason)
            ? "필요한 인식 신호가 없습니다."
            : input.UnknownReason.Trim();
        var state = State(profile, input);
        var phase = Phase(state);
        var blocker = Blocker(profile, input, state, unknownReason);
        var confidence = blocker.Length == 0
            ? "높음 · 현재 인벤토리와 수치 신호가 확인됨"
            : "낮음 · " + blocker;
        var fields = Fields(profile, input, unknownReason);
        return new BulletOperatingBoard(state, input.Round, phase, confidence,
            Action(state), Objective(profile, state), Routes(profile, state), Focus(state),
            Gate(state), blocker.Length == 0 ? null : blocker,
            blocker.Length == 0 ? null : "누락된 신호를 다시 인식한 뒤 현재 단계를 재평가합니다.",
            blocker, fields);
    }

    private static bool IsBullet(BulletStrategyProfile profile, string? goalId) =>
        string.Equals(goalId, profile.GoalUnitId, StringComparison.Ordinal);

    private static BulletOperatingBoardState State(BulletStrategyProfile profile,
        BulletOperatingBoardInput input)
    {
        if (input.FirstLegendFoundationKnown != true)
            return BulletOperatingBoardState.EarlyFoundation;
        if (input.FlyingCapableLegendCount is null or < 2)
            return BulletOperatingBoardState.AirMobility;
        if (input.BossKillUnitCount is null or < 2)
            return BulletOperatingBoardState.BossKill;
        if (input.ExternalSlow is null || input.AuraArmorReduction is null || input.Stun is null ||
            input.ExternalSlow < profile.ExternalSlowTarget ||
            input.AuraArmorReduction < profile.AuraArmorReductionTarget ||
            input.Stun < input.StunTarget)
            return BulletOperatingBoardState.ControlArmor;
        return input.Round >= profile.Round50CraftRound
            ? BulletOperatingBoardState.Round50
            : BulletOperatingBoardState.Ready;
    }

    private static string Phase(BulletOperatingBoardState state) => state switch
    {
        BulletOperatingBoardState.EarlyFoundation => "첫 전설 기반",
        BulletOperatingBoardState.AirMobility => "공중 전설",
        BulletOperatingBoardState.BossKill => "보스 처치",
        BulletOperatingBoardState.ControlArmor => "제어·방어",
        BulletOperatingBoardState.Ready => "준비 완료",
        _ => "라운드 50"
    };

    private static string Action(BulletOperatingBoardState state) => state switch
    {
        BulletOperatingBoardState.EarlyFoundation => "초반 첫 전설 기반을 확정하세요.",
        BulletOperatingBoardState.AirMobility => "공중 가능 전설 2/2를 먼저 만드세요.",
        BulletOperatingBoardState.BossKill => "보스 처치 전설을 2/2로 맞추세요.",
        BulletOperatingBoardState.ControlArmor => "감속·방어력 감소·기절 수치를 충족하세요.",
        BulletOperatingBoardState.Ready => "방어력 감소부터 강화하고 라운드 50 조합을 준비하세요.",
        _ => "라운드 50 Bullet 조합을 즉시 목표로 두세요."
    };

    private static string Objective(BulletStrategyProfile profile, BulletOperatingBoardState state) =>
        state switch
        {
            BulletOperatingBoardState.EarlyFoundation => "첫 전설 기반 1/1",
            BulletOperatingBoardState.AirMobility =>
                $"스토리 {profile.FlyingStoryBoundary} 또는 라운드 {profile.FlyingRoundBoundary} 전 공중 가능 전설 2/2",
            BulletOperatingBoardState.BossKill => "보스 처치 전설 2/2",
            BulletOperatingBoardState.ControlArmor => "외부 감속 82, 오라 방어력 감소 100, 기절 충족",
            BulletOperatingBoardState.Ready => "라운드 50 Bullet 조합 준비",
            _ => "라운드 50 Bullet 조합"
        };

    private static ImmutableArray<string> Routes(BulletStrategyProfile profile,
        BulletOperatingBoardState state) => state switch
    {
        BulletOperatingBoardState.EarlyFoundation =>
            ["첫 전설 우선순위에서 현재 패와 맞는 기반을 유지합니다."],
        BulletOperatingBoardState.AirMobility =>
            ["두 번째 전설까지 공중 가능 여부를 우선합니다.", "부족하면 세 번째 전설이 공중 자리를 채웁니다."],
        BulletOperatingBoardState.BossKill => ["보스 처치 전설만 2/2까지 보강합니다."],
        BulletOperatingBoardState.ControlArmor =>
            ["외부 감속 82를 확보합니다.", "오라 방어력 감소 100을 확보합니다.", "기절 목표를 Green Blood +0.3과 함께 확인합니다."],
        _ => ["방어력 감소 → 공격 속도 → 공격력 순서로 강화합니다.",
            "Bounty Hunter를 기본으로 추천합니다.", "Emergency Call은 대체 추천으로만 유지합니다."]
    };

    private static string Focus(BulletOperatingBoardState state) => state switch
    {
        BulletOperatingBoardState.ControlArmor => "Bullet 기여와 Green Blood 기절을 수치로 함께 확인",
        BulletOperatingBoardState.Ready or BulletOperatingBoardState.Round50 =>
            "방어력 감소 → 공격 속도 → 공격력",
        _ => "현재 단계의 필수 전설만 우선"
    };

    private static string Gate(BulletOperatingBoardState state) => state switch
    {
        BulletOperatingBoardState.EarlyFoundation => "공중 가능 전설 2/2 준비",
        BulletOperatingBoardState.AirMobility => "보스 처치 전설 2/2",
        BulletOperatingBoardState.BossKill => "감속·방어력 감소·기절 제어 수치 확인",
        BulletOperatingBoardState.ControlArmor => "라운드 50 Bullet 조합 또는 명시적 차단 사유",
        BulletOperatingBoardState.Ready => "라운드 50 Bullet 조합",
        _ => "조합 결과 확인 또는 명시적 차단 사유"
    };

    private static string Blocker(BulletStrategyProfile profile, BulletOperatingBoardInput input,
        BulletOperatingBoardState state, string unknownReason)
    {
        if (input.FirstLegendFoundationKnown is null ||
            state == BulletOperatingBoardState.EarlyFoundation && input.FirstLegendFoundationKnown == false)
            return "첫 전설 기반 확인 불가 — " + unknownReason;
        if (input.FlyingCapableLegendCount is null)
            return "공중 가능 전설 수량 확인 불가 — " + unknownReason;
        if (input.BossKillUnitCount is null)
            return "보스 처치 전설 수량 확인 불가 — " + unknownReason;
        if (input.ExternalSlow is null)
            return "외부 감속 수치 확인 불가 — " + unknownReason;
        if (input.AuraArmorReduction is null)
            return "오라 방어력 감소 수치 확인 불가 — " + unknownReason;
        if (input.Stun is null)
            return "기절 수치 확인 불가 — " + unknownReason;
        if (input.GreenBloodKnown is null)
            return "Green Blood 사용 상태 확인 불가 — " + unknownReason;
        if (input.BulletCrafted is null)
            return "Bullet 조합 상태 확인 불가 — " + unknownReason;
        if (string.IsNullOrWhiteSpace(input.EnhancementStatus))
            return "강화 단계 확인 불가 — " + unknownReason;
        return "";
    }

    private static ImmutableArray<BulletOperatingBoardField> Fields(
        BulletStrategyProfile profile, BulletOperatingBoardInput input, string unknownReason)
    {
        var flying = Current(input.FlyingCapableLegendCount,
            value => $"현재 {value}/2 · 목표 스토리 12 또는 라운드 50 전 2/2 · 부족 시 세 번째 전설이 공중을 채움",
            "목표 스토리 12 또는 라운드 50 전 2/2 · 부족 시 세 번째 전설이 공중을 채움",
            "공중 가능 전설", unknownReason);
        var boss = Current(input.BossKillUnitCount,
            value => $"현재 {value}/2 · 목표 2/2", "목표 2/2",
            "보스 처치 전설", unknownReason);
        var slow = Current(input.ExternalSlow,
            value => $"현재 {Number(value)}/82 · Bullet 고유 +{Number(profile.BulletSlowContribution)}",
            $"목표 82 · Bullet 고유 +{Number(profile.BulletSlowContribution)}",
            "외부 감속", unknownReason);
        var armor = Current(input.AuraArmorReduction,
            value => $"현재 {Number(value)}/100 · Bullet 정책 선언 기여 +{Number(profile.BulletArmorReductionContribution)}",
            $"목표 100 · Bullet 정책 선언 기여 +{Number(profile.BulletArmorReductionContribution)}",
            "오라 방어력 감소", unknownReason);
        var stun = Current(input.Stun,
            value => $"현재 {Number(value)} · 목표 {Number(input.StunTarget)} · Green Blood +{Number(profile.GreenBloodStunContribution)}",
            $"목표 {Number(input.StunTarget)} · Green Blood +{Number(profile.GreenBloodStunContribution)}",
            "기절", unknownReason);
        var craft = Current(input.BulletCrafted,
            value => $"현재 {(value ? "조합 완료" : "조합 전")} · 목표 라운드 50 Bullet 조합",
            "목표 라운드 50 Bullet 조합", "라운드 50 Bullet 조합", unknownReason);
        var enhancement = string.IsNullOrWhiteSpace(input.EnhancementStatus)
            ? Unknown("목표 방어력 감소 → 공격 속도 → 공격력", "강화 순서", unknownReason)
            : $"현재 {input.EnhancementStatus} · 목표 방어력 감소 → 공격 속도 → 공격력";
        const string navigation =
            "현재 권장 Bounty Hunter · 목표 Bounty Hunter · 대체 Emergency Call · 추천만 제공";
        return
        [
            Field(BulletOperatingBoardFieldKind.Flying, "bullet-board-flying", "공중 가능 전설", flying),
            Field(BulletOperatingBoardFieldKind.BossKill, "bullet-board-boss-kill", "보스 처치 전설", boss),
            Field(BulletOperatingBoardFieldKind.Slow, "bullet-board-slow", "외부 감속", slow),
            Field(BulletOperatingBoardFieldKind.ArmorReduction, "bullet-board-armor-reduction", "오라 방어력 감소", armor),
            Field(BulletOperatingBoardFieldKind.Stun, "bullet-board-stun", "기절", stun),
            Field(BulletOperatingBoardFieldKind.Craft, "bullet-board-craft", "라운드 50 Bullet 조합", craft),
            Field(BulletOperatingBoardFieldKind.Enhancement, "bullet-board-enhancement", "강화 순서", enhancement),
            Field(BulletOperatingBoardFieldKind.Navigation, "bullet-board-navigation", "항법 추천", navigation)
        ];
    }

    private static string Current<T>(T? value, Func<T, string> known, string target,
        string signal, string reason) where T : struct => value.HasValue
        ? known(value.Value)
        : Unknown(target, signal, reason);

    private static string Unknown(string target, string signal, string reason) =>
        $"알 수 없음 · {target} — {signal} {reason}";

    private static BulletOperatingBoardField Field(BulletOperatingBoardFieldKind kind,
        string id, string label, string value) =>
        new(kind, id, label, value, label, value,
            value.StartsWith("알 수 없음", StringComparison.Ordinal));

    private static string Number(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
