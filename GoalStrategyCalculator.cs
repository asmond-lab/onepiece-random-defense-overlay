using System.Globalization;
using System.Runtime.CompilerServices;

namespace OrandOverlay;

/// <summary>
/// 목표 상위의 전략 프로필 산정과 유닛 능력치 가중 합계 전담 계산기.
/// RecommendationEngine에서 동작 보존으로 추출된 클래스 — 로직·수치 변경 금지.
/// </summary>
internal static class GoalStrategyCalculator
{
    // 43747의 "[스턴]"은 역할 분류라 공격 타입이 사라진다. 후지토라는 마딜
    // 전설이므로 물딜 목표의 완성 보조로 남기지 않는다. 물딜 상위의 필수 조합
    // 재료로 소비되는 경로는 RecommendationEngine의 재료 클러스터가 별도로 다룬다.
    private static readonly HashSet<string> MagicDamageSupportRawcodes =
        new(StringComparer.Ordinal) { "130h" };
    private static readonly ConditionalWeakTable<UnitDefinition, StrongBox<StrategyMetrics>>
        StrategyMetricsCache = new();

    // TMO build-helper 43747 strategy baseline: full slow is 102 on both Divine/Nightmare.
    internal const double FullSlowTarget = 102;
    internal const double FullArmorReductionTarget = 211;
    internal const double StableStunTarget = 1.4;
    internal const double MaximumUsefulStun = 1.5;
    // 니카(루초·뱀초) 실측 216판: 이감 버전(스턴 1.6·이감 95)과 노이감 버전
    // (스턴 2.1·이감 40)이 갈린다. 패의 스턴이 이 값 이상이면 노이감으로 판정.
    internal const double NikaNoSlowCommitStun = 1.8;
    // 신+ 상디초월 클리어 92판 실측: 방깎 중앙값 0, 마방깎 중앙값 1(에넬·후지토라·우타
    // 경유, p75=18). 마딜 상위는 방깎 대신 마방깎 소스 최소 한 점만 확보하고, 큰 수치는
    // 채용률 정렬에 맡긴다.
    internal const double MagicArmorSourceTarget = 1;

    internal static StrategyMetrics StrategyMetricsFor(UnitDefinition unit) =>
        StrategyMetricsCache.GetValue(unit,
            static value => new StrongBox<StrategyMetrics>(
                CalculateStrategyMetrics(value))).Value;

    private static StrategyMetrics CalculateStrategyMetrics(UnitDefinition unit)
    {
        var slow = AbilitySignedTotal(unit, "이동속도 감소", "발동이동속도 감소");
        var stun = AbilityTotal(unit, "스턴");
        // 거프 불멸의 적 방어 +15는 방깎 -15다. 절댓값으로 합치면 깎이 30 과대평가된다.
        var armor = AbilitySignedTotal(unit, "방어력 감소") +
                    AbilityTotal(unit, "발동방어력 감소", "중첩방어력 감소");
        // TMO 43747 베르고: 마나 스킬의 공성업 비례 방깎은 최대 30.
        // 지속 스턴으로 환산할 수 없는 액티브지만 방깎 마감 계산에는 포함한다.
        if (unit.Rawcodes.Contains("W30h", StringComparer.Ordinal)) armor += 30;
        var magicArmor = AbilityTotal(unit, "마법방어력 감소");
        var armorBreak = AbilityPresenceOrTotal(unit, "아머브레이크", "단일아머브레이크");
        var airMovement = AbilityPresenceOrTotal(unit, "공중이동");
        var boss = AbilityTotal(unit, "보스 잡기");
        var berserkBoss = AbilityTotal(unit, "광폭화 잡기");
        // 딜 유형은 수치보다 "몇 기 보유"가 중요하므로 존재를 1로 센다.
        var singleDamage = AbilityPresenceOrTotal(unit, "단일") > 0 ? 1 : 0;
        var finisherDamage = AbilityPresenceOrTotal(unit, "끝딜") > 0 ? 1 : 0;
        return new StrategyMetrics(slow, stun, armor, armorBreak, airMovement, boss, berserkBoss,
            magicArmor, singleDamage, finisherDamage);
    }

    internal static GoalStrategyProfile? StrategyProfileFor(UnitDefinition goal,
        double committedStun = 0, string buildVariant = BuildVariants.AutoId)
    {
        var rawcode = goal.Rawcodes.FirstOrDefault() ?? "";
        // 2.314 recent-community profiles. A zero target means the selected top unit can
        // clear without reserving a separate support slot; the core 102 slow / 1.4 stun /
        // 211 armor targets still take precedence. Unknown physical tops stay conservative.
        GoalStrategyProfile? profile = null;
        if (goal.Id.Equals("yamato_transcendent", StringComparison.OrdinalIgnoreCase) ||
            rawcode.Equals("DB0H", StringComparison.Ordinal))
            profile = new GoalStrategyProfile(0, 0, StunBeforeSlow: true);
        else if (rawcode.Equals("B90H", StringComparison.Ordinal)) // Usopp: self boss/berserk.
            profile = new GoalStrategyProfile(1, 1);
        else if (rawcode.Equals("F90H", StringComparison.Ordinal)) // Zoro: Croc limit OR Bon Clay.
            profile = new GoalStrategyProfile(0, 0, true, CommunityCoreTarget: 1);
        else if (rawcode.Equals("A90H", StringComparison.Ordinal)) // Jinbe: self + one armor break.
            profile = new GoalStrategyProfile(1, 1, ArmorBreakTarget: 2);
        else if (rawcode.Equals("B50h", StringComparison.Ordinal)) // Cavendish: no forced extra.
            profile = new GoalStrategyProfile(0, 0);
        else if (rawcode.Equals("490H", StringComparison.Ordinal)) // Basil: recent reports need help.
            profile = new GoalStrategyProfile(2, 1);
        else if (rawcode.Equals("I70h", StringComparison.Ordinal)) // Katakuri: full armor for both.
            profile = new GoalStrategyProfile(1, 1, FillCommunitySupports: true,
                CommunityCoreTarget: ClearBuildStats.CoreCandidateLimit);
        else if (rawcode.Equals("C40h", StringComparison.Ordinal))
            // 거프: 공중이동 슬롯을 안 쓰고 깎·버프에 쓴다. 스턴·짤필러는 추론이 채운다.
            profile = new GoalStrategyProfile(1, 1, FillCommunitySupports: true,
                AirMovementTarget: 0);
        else if (rawcode.Equals("750h", StringComparison.Ordinal))
            // 비비 영원 장인 공략: 자체 광보잡을 인정하고 토키·키쿠·모비딕 및
            // 상디/키드 한 기에 자원을 집중한다. 물딜 방깎·마방깎은 강제하지 않는다.
            profile = new GoalStrategyProfile(1, 1, FillCommunitySupports: true,
                SlowTarget: 80, ArmorReductionTarget: 0,
                MagicArmorReductionTarget: 0);
        else if (goal.Rawcodes.Any(code => code is "KB0H" or "KB0H_"))
        {
            // 니카(루초·뱀초): 신+ 216판이 이감 버전(스턴 1.6)과 노이감(2.9)으로 갈린다.
            var noSlow = buildVariant == "noslow" ||
                         buildVariant != "slow" && committedStun >= NikaNoSlowCommitStun;
            profile = noSlow
                ? new GoalStrategyProfile(1, 1, SlowTarget: 40, StunTarget: 2.9, StunCap: 3.0)
                : new GoalStrategyProfile(1, 1, StunTarget: 1.6, StunCap: 1.7);
        }
        else if (rawcode.Equals("E90H", StringComparison.Ordinal)) // Doflamingo: zero self-stun.
            profile = new GoalStrategyProfile(1, 1);
        else if (IsMagicDamageTier(goal.Tier))
        {
            // 마딜 상위는 물딜 방깎 파이프라인을 타면 안 된다.
            // 신+ 상디 2451판: 광보잡 1기 42% · 2기 28% · 2기 이상 37%.
            var goalSingle = AbilityTotal(goal, "단일");
            var goalFinisher = AbilityTotal(goal, "끝딜");
            profile = new GoalStrategyProfile(1, 2, FillCommunitySupports: true,
                ArmorReductionTarget: 0, MagicArmorReductionTarget: MagicArmorSourceTarget,
                SingleDamageTarget: goalFinisher > 0 && goalSingle <= 0 ? 1 : 0,
                FinisherDamageTarget: goalSingle > 0 && goalFinisher <= 0 ? 1 : 0);
        }
        else
        {
            var isPhysicalTop = goal.OfficialAbilities.Any(ability =>
                ability.Name.Equals("바제스", StringComparison.Ordinal) &&
                ability.DisplayValue.Equals("가능", StringComparison.Ordinal));
            if (isPhysicalTop) profile = new GoalStrategyProfile(1, 1);
        }

        var inferred = InferSupportNeeds(profile, goal);
        // 모든 물딜 상위는 화면 순서대로 제작해도 생존 코어가 먼저 완성되어야 한다.
        // 스턴을 채용률보다 앞세우고, 방깎은 211을 넘기는 최소 기물 세트까지만
        // 추천한다. 마딜은 마방깎·버퍼 파이프라인을 그대로 유지한다.
        return inferred is { } physical && IsPhysicalDamageGoal(goal)
            ? physical with
            {
                PrioritizeStunRecommendations = true,
                MinimizeArmorRecommendationSet = true,
                StopAfterCoreTargets = true,
                ArmorBeforeSlow = true,
                StunBeforeSlow =
                    physical.StunTarget > AbilityTotal(goal, "스턴") + 0.0001
            }
            : inferred;
    }

    /// <summary>
    /// 43747 공략 문구와 자체 스턴·이감으로 상위별 목표를 보정한다.
    /// 거프처럼 손댄 규칙(자체 원스턴이면 추가 스턴 생략, 솔딜이면 커뮤니티 코어,
    /// 짤이감·짤깍)을 다른 상위에도 같은 근거로 적용한다.
    /// </summary>
    private static GoalStrategyProfile? InferSupportNeeds(GoalStrategyProfile? profile,
        UnitDefinition goal)
    {
        if (profile is null) return null;
        var p = profile.Value;
        var text = goal.Description ?? "";
        var selfStun = AbilityTotal(goal, "스턴");
        var selfSlow = AbilitySignedTotal(goal, "이동속도 감소", "발동이동속도 감소");

        if (p.SlowTarget >= 80 || p.ArmorReductionTarget >= 100)
            p = p with { PreferCheapStatFillers = true };

        var stunUnreliable = text.Contains("원스턴 불안", StringComparison.Ordinal) ||
                             text.Contains("스턴 부족", StringComparison.Ordinal);
        var skipSlowAndStun = text.Contains("이감·스턴 없이", StringComparison.Ordinal) ||
                              text.Contains("이감 스턴 없이", StringComparison.Ordinal);
        var selfSlowCovers = text.Contains("혼자이감커버", StringComparison.Ordinal);
        var soloCarry = text.Contains("솔딜", StringComparison.Ordinal);
        var buffScaler = text.Contains("버프 개수 비례", StringComparison.Ordinal) ||
                         text.Contains("버프개수비례", StringComparison.Ordinal);

        if (skipSlowAndStun)
            p = p with { StunTarget = 0, StunBeforeSlow = false, SlowTarget = 0 };
        else if (!stunUnreliable && selfStun >= 1.1 &&
                 p.StunTarget <= StableStunTarget + 0.05)
            p = p with { StunTarget = Math.Max(p.StunTarget, selfStun), StunBeforeSlow = false };

        if (selfSlowCovers && p.SlowTarget >= FullSlowTarget - 0.1)
            p = p with { SlowTarget = Math.Clamp(selfSlow + 20, 80, FullSlowTarget) };

        if ((soloCarry || buffScaler) && !p.StopAfterCoreTargets)
            p = p with { FillCommunitySupports = true };

        return p;
    }

    internal static bool IsMagicDamageTier(string tier) => DamageTiers.IsMagic(tier);

    internal static bool IsPhysicalDamageTier(string tier) =>
        tier.Contains("[물딜]", StringComparison.Ordinal);

    private static bool IsPhysicalDamageGoal(UnitDefinition goal) =>
        !IsMagicDamageTier(goal.Tier) &&
        (IsPhysicalDamageTier(goal.Tier) ||
         goal.Rawcodes.Contains("DB0H", StringComparer.Ordinal) ||
         goal.OfficialAbilities.Any(ability =>
             ability.Name.Equals("바제스", StringComparison.Ordinal) &&
             !ability.DisplayValue.Equals("불가", StringComparison.OrdinalIgnoreCase)));

    internal static bool IsCompatibleTopDamageType(UnitDefinition goal,
        UnitDefinition candidate)
    {
        if (IsMagicDamageTier(goal.Tier))
            return IsMagicDamageTier(candidate.Tier);
        if (IsPhysicalDamageGoal(goal))
            return IsPhysicalDamageGoal(candidate);
        return true;
    }

    internal static bool IsCompatibleSupportDamageType(UnitDefinition goal,
        UnitDefinition candidate) =>
        !IsPhysicalDamageGoal(goal) ||
        !IsMagicDamageTier(candidate.Tier) &&
        !candidate.Rawcodes.Any(MagicDamageSupportRawcodes.Contains);

    /// <summary>신+ 오로성(판별 전역 변수)에 맞춰 역할 목표를 보정한다.</summary>
    internal static GoalStrategyProfile? ApplyGorosei(GoalStrategyProfile? strategy,
        GoroseiMode gorosei)
    {
        if (strategy is null || gorosei == GoroseiMode.None) return strategy;
        var adjusted = strategy.Value with
        {
            SlowTarget = GoroseiEffects.AdjustSlowTarget(strategy.Value.SlowTarget, gorosei),
            ArmorReductionTarget =
                GoroseiEffects.AdjustArmorTarget(strategy.Value.ArmorReductionTarget, gorosei),
            MagicArmorReductionTarget =
                GoroseiEffects.AdjustMagicArmorTarget(strategy.Value.MagicArmorReductionTarget,
                    gorosei)
        };
        // 새턴은 아군 공격력·폭뎀을 깎으므로 마딜 상위는 단일·끝딜을 모두 갖춘다.
        // (상위 자체 보유분은 projected에 합산되어 자동 충족된다.)
        if (gorosei == GoroseiMode.Saturn && adjusted.MagicArmorReductionTarget > 0)
            adjusted = adjusted with
            {
                SingleDamageTarget = Math.Max(1, adjusted.SingleDamageTarget),
                FinisherDamageTarget = Math.Max(1, adjusted.FinisherDamageTarget)
            };
        return adjusted;
    }

    internal static double AbilityTotal(UnitDefinition unit, params string[] abilityNames) =>
        unit.OfficialAbilities
            .Where(ability => abilityNames.Contains(ability.Name, StringComparer.Ordinal))
            .Sum(ability => AbilityNumber(ability.DisplayValue));

    private static double AbilitySignedTotal(UnitDefinition unit, params string[] abilityNames) =>
        unit.OfficialAbilities
            .Where(ability => abilityNames.Contains(ability.Name, StringComparer.Ordinal))
            .Sum(ability => double.TryParse(ability.DisplayValue, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) ? value : 0);

    private static double AbilityPresenceOrTotal(UnitDefinition unit, params string[] abilityNames) =>
        unit.OfficialAbilities
            .Where(ability => abilityNames.Contains(ability.Name, StringComparer.Ordinal))
            .Sum(ability => Math.Max(1, AbilityNumber(ability.DisplayValue)));

    private static double AbilityNumber(string displayValue)
    {
        if (displayValue.Equals("가능", StringComparison.Ordinal)) return 1;
        return double.TryParse(displayValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Abs(value)
            : 0;
    }
}

/// <summary>목표 상위별 전략 목표치(엔진에서 동작 보존으로 추출한 레코드).</summary>
internal readonly record struct GoalStrategyProfile(double BossControlTarget,
    double BerserkBossControlTarget, bool OptionalBossSupportAfterCore = false,
    bool FillCommunitySupports = false, double SlowTarget = GoalStrategyCalculator.FullSlowTarget,
    double StunTarget = GoalStrategyCalculator.StableStunTarget,
    double ArmorReductionTarget = GoalStrategyCalculator.FullArmorReductionTarget,
    double ArmorBreakTarget = 0, int CommunityCoreTarget = 0, bool StunBeforeSlow = true,
    double AirMovementTarget = 1, double MagicArmorReductionTarget = 0,
    double SingleDamageTarget = 0, double FinisherDamageTarget = 0,
    double StunCap = GoalStrategyCalculator.MaximumUsefulStun,
    bool PreferCheapStatFillers = false, bool PrioritizeStunRecommendations = false,
    bool MinimizeArmorRecommendationSet = false, bool StopAfterCoreTargets = false,
    bool ArmorBeforeSlow = false);

/// <summary>보유 패의 전략 지표 합산 값(엔진에서 동작 보존으로 추출한 레코드).</summary>
internal readonly record struct StrategyMetrics(double Slow = 0, double Stun = 0,
    double ArmorReduction = 0, double ArmorBreak = 0, double AirMovement = 0,
    double BossControl = 0, double BerserkBossControl = 0, double MagicArmorReduction = 0,
    double SingleDamage = 0, double FinisherDamage = 0)
{
    public bool HasAny => Total > 0;
    public double Total => Slow + Stun + ArmorReduction + ArmorBreak + AirMovement +
                           BossControl + BerserkBossControl + MagicArmorReduction +
                           SingleDamage + FinisherDamage;

    public static StrategyMetrics operator +(StrategyMetrics left, StrategyMetrics right) =>
        new(left.Slow + right.Slow, left.Stun + right.Stun,
            left.ArmorReduction + right.ArmorReduction, left.ArmorBreak + right.ArmorBreak,
            left.AirMovement + right.AirMovement,
            left.BossControl + right.BossControl,
            left.BerserkBossControl + right.BerserkBossControl,
            left.MagicArmorReduction + right.MagicArmorReduction,
            left.SingleDamage + right.SingleDamage,
            left.FinisherDamage + right.FinisherDamage);

    public static StrategyMetrics operator *(StrategyMetrics value, int multiplier) =>
        new(value.Slow * multiplier, value.Stun * multiplier,
            value.ArmorReduction * multiplier, value.ArmorBreak * multiplier,
            value.AirMovement * multiplier, value.BossControl * multiplier,
            value.BerserkBossControl * multiplier, value.MagicArmorReduction * multiplier,
            value.SingleDamage * multiplier, value.FinisherDamage * multiplier);
}
