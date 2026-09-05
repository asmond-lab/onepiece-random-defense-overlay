using System.Globalization;

namespace OrandOverlay;

public sealed class RecommendationEngine(DataCatalog catalog, ClearBuildStats? clearStats = null,
    CombineHotkeyCatalog? combineHotkeys = null)
{
    // RecommendNearestCrafts 진입 시 항법에 따라 설정된다. 1상위 항법이면 1상위
    // 클리어만, 긴급소집 같은 다상위 항법이면 상위 2기 이상 클리어만 집계한
    // 프로필을 쓴다(표본 부족 시 전체로 후퇴).
    private TopScope _topScope;

    // 이번 패스의 스턴 공략 목표·상한 — 패 수치 카드가 고정 1.4 대신 이 값을 쓴다.
    // (기본 1.4/1.5, 니카 이감 1.6/1.7, 노이감 2.9/3.0 등 공략마다 다르다.)
    public double ActiveStunTarget { get; private set; } = StableStunTarget;
    public double ActiveStunCap { get; private set; } = MaximumUsefulStun;
    // 이번 추천 패스에서 쓸 클리어 프로필. 마딜 목표는 보유 앵커(이미 짠 취향 유닛)와
    // 같이 쓰인 클리어만 재집계한 조건부 프로필일 수 있다.
    private GoalClearProfile? _activeClearProfile;
    private LiveStats _liveStats = new();
    // 조합 트리·조합식 등급 조회 전담 빌더(동작 보존 추출).
    private readonly RecipeTreeBuilder _recipes = new(catalog, combineHotkeys);
    // 최하위 재료 전개는 카탈로그 수명 동안 불변이다. 엔진 호출마다 계산기를
    // 새로 만들어 leaf cache를 버리지 않고 재사용한다.
    private readonly RecipeCompletionCalculator _recipeCalculator = new(catalog.Unit);
    // 같은 자동 인식 패가 반복될 때 후보별 RecipeProgress를 다시 전개하지 않는다.
    // 패가 하나라도 바뀌면 전체 폐기하는 단일 스냅샷 캐시라 stale 결과가 남지 않는다.
    private Dictionary<string, int> _candidateProgressInventory =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecipeProgress> _candidateProgressCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>자체 수집 통계의 게이트 통과 가중을 화면 순서 점수에 반영하도록 연결한다.</summary>
    public void SetLiveStats(LiveStats liveStats) => _liveStats = liveStats;
    private string? _activeAnchorLabel;

    // GoalStrategyCalculator로 이동한 전략 상수의 엔진 내 별칭(호출점 무변경 유지).
    private const double StableStunTarget = GoalStrategyCalculator.StableStunTarget;
    private const double MaximumUsefulStun = GoalStrategyCalculator.MaximumUsefulStun;
    private const double FullSlowTarget = GoalStrategyCalculator.FullSlowTarget;
    private const double FullArmorReductionTarget = GoalStrategyCalculator.FullArmorReductionTarget;
    private const double NikaNoSlowCommitStun = GoalStrategyCalculator.NikaNoSlowCommitStun;
    private const double MagicArmorSourceTarget = GoalStrategyCalculator.MagicArmorSourceTarget;

    private const double CompletionWeight = 34;
    private const double RoleWeight = 36;
    // 니카(루초·뱀초) 실측 216판: 이감 버전(스턴 1.6·이감 95)과 노이감 버전
    // 신+ 상디초월 클리어 92판 실측: 방깎 중앙값 0, 마방깎 중앙값 1(에넬·후지토라·우타
    // 경유, p75=18). 마딜 상위는 방깎 대신 마방깎 소스 최소 한 점만 확보하고, 큰 수치는

    public IReadOnlyList<Recommendation> Recommend(
        string goalUnitId,
        IEnumerable<InventoryEntry> inventory,
        int take = 3)
    {
        var inventoryList = inventory.ToList();
        var counts = inventoryList
            .GroupBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count), StringComparer.OrdinalIgnoreCase);
        RecipeWildcards.AddSyntheticCounts(counts, catalog.Unit);

        var ownedRoles = AggregateRoles(counts);
        var recipeCalculator = _recipeCalculator;
        return catalog.Data.Routes
            .Where(x => x.GoalUnitId.Equals(goalUnitId, StringComparison.OrdinalIgnoreCase))
            .Select(route => Evaluate(route, counts, ownedRoles, recipeCalculator))
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToList();
    }

    public IReadOnlyList<Recommendation> RecommendNearestCrafts(
        string goalUnitId,
        IEnumerable<InventoryEntry> inventory,
        int take = 8,
        string navigationMode = "PathOfKings.BountyHunter",
        GoroseiMode gorosei = GoroseiMode.None,
        string buildVariant = BuildVariants.AutoId,
        bool suppressSeraphim = false,
        bool prioritizeTargetRare = false,
        bool suppressFirstRareShip = false,
        bool suppressSecondaryTopCandidates = false,
        string difficulty = "unknown")
    {
        var inventoryList = inventory.ToList();
        var counts = inventoryList
            .GroupBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Count),
                StringComparer.OrdinalIgnoreCase);
        RecipeWildcards.AddSyntheticCounts(counts, catalog.Unit);
        PrepareCandidateProgressCache(counts);
        _shipNeedCache.Clear();
        var calculator = _recipeCalculator;
        var initialCandidateEvaluations = new Dictionary<string, Recommendation>(
            StringComparer.OrdinalIgnoreCase);
        var goal = catalog.Unit(goalUnitId);
        var legendOnly = BaseTier(goal.Tier) == "전설";
        if (legendOnly) take = 1;
        var goalSuggestion = EvaluateCraft(goal, counts, calculator);
        // 니카 루초/뱀초처럼 인게임 rawcode를 공유하는 목표는 어느 쪽으로 인식돼도
        // 보유로 판정한다.
        var goalOwned = counts.GetValueOrDefault(goalUnitId) > 0 ||
                        goal.Rawcodes.Any(code =>
                            counts.GetValueOrDefault("rawcode:" + code) > 0);
        var navigation = NavigationProfiles.Find(navigationMode);
        _topScope = navigation.AllowsMultipleTopUnits ? TopScope.MultiTop
            : navigation.CanCraftTopUnits ? TopScope.SoloTop
            : TopScope.Any;
        var recipeLegendaryIds = _recipes.RecipeLegendaryIds(goal);
        IReadOnlyList<string> pinnedRecipeLegendaryIds =
            goalOwned ? [] : recipeLegendaryIds;

        // 취향 조건부 학습: 이미 짠 지원급 유닛을 앵커로, 그 유닛과 같이 쓰인
        // 클리어만 골라 채용률을 재계산한다(마딜에서 검증 후 전 상위로 확대 —
        // 뱀초의 이감/노이감처럼 물딜도 행보가 갈린다). 목박 필러는 앵커가 아니다.
        var ownedAnchorCodes = counts.Where(pair => pair.Value > 0)
            .Select(pair => catalog.Unit(pair.Key))
            .Where(unit => CountsAsCompletedSupport(unit.Tier))
            .SelectMany(unit => unit.Rawcodes)
            .Where(code => !goal.Rawcodes.Contains(code, StringComparer.Ordinal))
            .Where(code => !LeftoverFillerRawcodes.Contains(code))
            .ToList();
        _activeClearProfile = clearStats?.ResolveProfile(goal.Rawcodes, _topScope,
            ownedAnchorCodes);
        _activeAnchorLabel = _activeClearProfile?.AnchorRawcodes is { Count: > 0 } anchorCodes
            ? string.Join("·", anchorCodes.Select(code => catalog.Unit("rawcode:" + code).Name))
            : null;

        // 키자루+특성공학은 특포가 키자루 스킬강화에 계속 들어가므로,
        // 특강(필수) 상위는 특포 경합으로 추가 상위 후보에서 제외한다.
        var avoidTraitHungryTops =
            goal.Rawcodes.Contains("5B0H", StringComparer.Ordinal) &&
            navigation.Id.Equals("AlliedForces.TraitEngineering", StringComparison.OrdinalIgnoreCase);
        // 그린블러드는 판당 1회용 — 이미 세라핌을 만들었거나(보유 세라핌 존재),
        // 유닛에 부여했으면(사용됨 신호·가상 buff 항목) 세라핌은 더 만들 수 없다.
        var seraphimBlocked = suppressSeraphim ||
                              counts.GetValueOrDefault("greenblood_buff") > 0 ||
                              catalog.AllUnits
                                  .Where(unit => unit.Tier.Split('[', 2)[0].Trim() == "세라핌")
                                  .Any(unit => counts.GetValueOrDefault(unit.Id) > 0);
        // 상위 기물 대깨 초기 빌드용: 목표 트리의 희귀함 중 가장 가까운 한기를 추가 노출한다
        // (패스트 유니크 퀘스트 대응). 기존 추천은 밀리지 않고 보드가 한 칸 길어진다.
        var rareShipTreeIds = _recipes.RareShipTreeIds(goal);
        var missingTargetRareIds = rareShipTreeIds
            .Where(id => counts.GetValueOrDefault(id) <= 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pinningTargetRare = !goalOwned && prioritizeTargetRare &&
                                missingTargetRareIds.Count > 0;
        var pinningFirstRareShip = !goalOwned && !suppressFirstRareShip &&
                                   !prioritizeTargetRare && rareShipTreeIds.Count > 0 &&
                                   !counts.Any(pair =>
                                       pair.Value > 0 && BaseTier(catalog.Unit(pair.Key).Tier) == "희귀함");

        var showGoal = (navigation.CanCraftTopUnits || legendOnly) && !goalOwned;
        var viviPartnerId = SelectViviExpertPartner(
            goal, counts, calculator, navigation.AllowsMultipleTopUnits);
        // 목표 자체 스턴 + 패에 쌓인 스턴으로 빌드 방향(니카 이감/노이감)을 판정한다.
        // 보유한 목표의 스턴은 집계에 이미 포함되고, 조합 예정이면 여기서 더한다.
        var committedStun = AggregateStrategyMetrics(counts).Stun +
                            (showGoal ? GoalStrategyCalculator.StrategyMetricsFor(goal).Stun : 0);
        var strategy = GoalStrategyCalculator.ApplyGorosei(
            GoalStrategyCalculator.StrategyProfileFor(goal, committedStun, buildVariant), gorosei);
        if (viviPartnerId == "rawcode:4B0H" && strategy is { } viviKidStrategy)
            strategy = viviKidStrategy with { StunTarget = 0, StunCap = 0 };
        ActiveStunTarget = strategy?.StunTarget ?? StableStunTarget;
        ActiveStunCap = strategy?.StunCap ?? MaximumUsefulStun;
        // 키자루 초월 + 역발상: 레일리는 확정 획득이지만 특성포인트가 부족해 자체
        // 딜이 약하다(유저 검증 · 가이드는 특성공학 추천). 단일·끝딜 보강으로
        // 라인딜 공백을 메운다. 클리어 기록엔 항법 흔적이 없어 항법 선택으로 반영.
        if (goal.Rawcodes.Contains("5B0H", StringComparer.Ordinal) &&
            navigation.Id.Equals("BestHelp.ReverseThinking", StringComparison.OrdinalIgnoreCase) &&
            strategy is { } kizaruStrategy)
            strategy = kizaruStrategy with
            {
                SingleDamageTarget = Math.Max(1, kizaruStrategy.SingleDamageTarget),
                FinisherDamageTarget = Math.Max(1, kizaruStrategy.FinisherDamageTarget)
            };
        var includeBuffCandidates = strategy is
        {
            PreferCheapStatFillers: true,
            ArmorReductionTarget: <= 0
        };
        var candidates = catalog.AllUnits
            .Where(unit => !unit.Id.Equals(goalUnitId, StringComparison.OrdinalIgnoreCase))
            .Where(unit => counts.GetValueOrDefault(unit.Id) <= 0)
            .Where(unit => !prioritizeTargetRare || BaseTier(unit.Tier) != "희귀함")
            .Where(unit => !seraphimBlocked || unit.Tier.Split('[', 2)[0].Trim() != "세라핌")
            .Where(unit => MeetsOwnedPrerequisites(unit, counts))
            .Where(unit => IsRecommendedCraftTier(unit.Tier, navigation.AllowsMultipleTopUnits) ||
                           IsCheapFillerFor(goal, unit))
            .Where(unit => GoalStrategyCalculator.IsCompatibleSupportDamageType(goal, unit))
            .Where(unit => !suppressSecondaryTopCandidates ||
                           !IsTopTier(unit.Tier) ||
                           unit.Id.Equals(viviPartnerId,
                               StringComparison.OrdinalIgnoreCase) ||
                           GoalStrategyCalculator.IsMagicDamageTier(goal.Tier) &&
                           GoalStrategyCalculator.StrategyMetricsFor(unit)
                               .MagicArmorReduction > 0)
            .Where(unit => !IsTopTier(unit.Tier) ||
                           GoalStrategyCalculator.IsCompatibleTopDamageType(goal, unit))
            .Where(unit => !avoidTraitHungryTops ||
                           !unit.Rawcodes.Any(TraitHungryTopRawcodes.Contains))
            .Where(unit => !AvoidTraitPointCraftWithoutEconomy(navigation, unit, counts))
            .Where(unit => IsViviExpertCompatible(
                goal, unit, viviPartnerId, goalOwned))
            .Where(unit => unit.Recipe.Count > 0)
            .Select(unit => (Unit: unit,
                Metrics: GoalStrategyCalculator.StrategyMetricsFor(unit)))
            // 역할 파이프라인이 절대 소비하지 않을 후보는 비싼 레시피 전개 전에 뺀다.
            .Where(candidate => strategy is null ||
                                candidate.Metrics.HasAny ||
                                includeBuffCandidates && IsCheapBuffFiller(candidate.Unit) ||
                                strategy.Value.FillCommunitySupports &&
                                (BuffSupportValue(candidate.Unit) > 0 ||
                                 CommunityPriorityScore(goal, candidate.Unit) > 0))
            .Select(candidate => new CraftCandidate(candidate.Unit,
                EvaluateInitialCandidate(candidate.Unit), candidate.Metrics))
            .Where(candidate => candidate.Recommendation.RecipeProgress.RequiredLeafCount > 0)
            .ToList();
        var projectedStun = AggregateStrategyMetrics(counts).Stun +
                            (showGoal ? GoalStrategyCalculator.StrategyMetricsFor(goal).Stun : 0);
        var nearestStunCraft = OrderByCraftDistance(candidates
                .Where(candidate => candidate.Metrics.Stun > 0))
            .FirstOrDefault();
        var hasTacticalStunRoute = nearestStunCraft is not null &&
            (nearestStunCraft.Recommendation.RecipeProgress.CompletionRatio >= 0.9999 ||
             nearestStunCraft.Unit.Recipe.Any(material => material.Value > 0 &&
                 counts.GetValueOrDefault(material.Key) >= material.Value &&
                 catalog.Unit(material.Key).Recipe.Count == 0 &&
                 BaseTier(catalog.Unit(material.Key).Tier) == "기타"));
        if (strategy is { PrioritizeStunRecommendations: true, StunTarget: <= 0 } tacticalStrategy &&
            projectedStun + 0.0001 < StableStunTarget &&
            hasTacticalStunRoute)
        {
            strategy = tacticalStrategy with
            {
                StunTarget = StableStunTarget,
                StunCap = MaximumUsefulStun,
                StunBeforeSlow = true
            };
            ActiveStunTarget = strategy.Value.StunTarget;
            ActiveStunCap = strategy.Value.StunCap;
        }

        var missingLegendaryIds = pinnedRecipeLegendaryIds
            .Where(id => counts.GetValueOrDefault(id) <= 0)
            .ToList();
        // UI에서 상위를 직접 골랐으면 현재 패에서 가장 가까운 미보유 희귀함을
        // 첫 행동으로 둔다. 이 고정 슬롯도 총 보드 한도 안에서 미리 예약한다.
        Recommendation? rarePick = null;
        if (pinningFirstRareShip || pinningTargetRare)
        {
            rarePick = (pinningTargetRare ? missingTargetRareIds : rareShipTreeIds)
                .Select(id => EvaluateInitialCandidate(catalog.Unit(id)))
                .Where(item => pinningTargetRare || MeetsOwnedPrerequisites(
                    catalog.Unit(item.Route.GoalUnitId), counts))
                .OrderBy(item => item.RecipeProgress.MissingLeaves
                    .Sum(leaf => leaf.MissingCount))
                .ThenByDescending(item => item.RecipeProgress.CompletionRatio)
                .FirstOrDefault();
        }

        var effectiveTake = take;
        var maximumSupports = Math.Max(0, effectiveTake - (showGoal ? 1 : 0));
        List<Recommendation> nearest;
        if (strategy is { } physicalStrategy)
        {
            const int maximumCoreBoardSize = 12;
            while (true)
            {
                nearest = OrderStrategySupports(goal, counts, candidates, maximumSupports,
                    physicalStrategy, navigation.AllowsMultipleTopUnits,
                    navigation.CanCraftTopUnits);
                var fitsCurrentBoard = nearest.Count <= maximumSupports;
                if (!physicalStrategy.StopAfterCoreTargets ||
                    fitsCurrentBoard &&
                    MeetsCoreTargets(goal, counts, nearest, showGoal, physicalStrategy) ||
                    effectiveTake >= maximumCoreBoardSize)
                    break;
                effectiveTake++;
                maximumSupports = Math.Max(0, effectiveTake - (showGoal ? 1 : 0));
            }
        }
        else
        {
            nearest = OrderCompatibleByCraftDistance(
                goal, counts, candidates, maximumSupports);
        }
        if (IsViviEternal(goal))
            nearest = AddViviExpertSupports(
                nearest, candidates, viviPartnerId, counts, goalOwned,
                EvaluateInitialCandidate);

        // 초월은 하위 전설을 먼저 짜야 스토리를 민다. 역할 패키지보다 후보 보드 앞에 둔다.
        if (missingLegendaryIds.Count > 0)
        {
            var missingLegendaries = missingLegendaryIds
                .Select(id => EvaluateInitialCandidate(catalog.Unit(id)))
                .ToList();
            var pinned = new HashSet<string>(missingLegendaries.Select(item => item.Route.GoalUnitId),
                StringComparer.OrdinalIgnoreCase);
            nearest = missingLegendaries
                .Concat(nearest.Where(item => !pinned.Contains(item.Route.GoalUnitId)))
                .ToList();
            effectiveTake += missingLegendaries.Count;
        }

        if (rarePick is not null)
        {
            if (!pinningTargetRare && rarePick is not null &&
                !nearest.Any(item => string.Equals(item.Route.GoalUnitId, rarePick.Route.GoalUnitId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                nearest.Insert(Math.Min(nearest.Count, recipeLegendaryIds.Count),
                    rarePick);
            }
        }

        var projectedBeforeSupports = AggregateStrategyMetrics(counts) +
                                      (showGoal
                                          ? GoalStrategyCalculator.StrategyMetricsFor(goal)
                                          : default);
        var stunPending = strategy is { PrioritizeStunRecommendations: true } activeStrategy &&
                          projectedBeforeSupports.Stun + 0.0001 < activeStrategy.StunTarget;
        bool IsActiveCommunityCore(Recommendation recommendation) =>
            strategy is { CommunityCoreTarget: > 0 } coreStrategy &&
            IsCommunityCore(goal,
                catalog.Unit(recommendation.Route.GoalUnitId), coreStrategy);
        nearest = nearest
            .Select((recommendation, index) => (recommendation, index))
            .OrderByDescending(pair =>
                recipeLegendaryIds.Contains(pair.recommendation.Route.GoalUnitId)
                    ? 4
                    : ViviExpertPriority(
                        goal, pair.recommendation.Route.GoalUnitId,
                        viviPartnerId, goalOwned) > 0
                        ? 3
                    : stunPending &&
                      IsActiveCommunityCore(pair.recommendation) &&
                      GoalStrategyCalculator.StrategyMetricsFor(
                          catalog.Unit(pair.recommendation.Route.GoalUnitId)).Stun > 0
                        ? 3
                    : stunPending && GoalStrategyCalculator.StrategyMetricsFor(
                        catalog.Unit(pair.recommendation.Route.GoalUnitId)).Stun > 0
                        ? 2
                        : IsActiveCommunityCore(pair.recommendation)
                            ? 1
                        : 0)
            // 물딜의 같은 생존 단계에서는 방깎 후보를 이감·보조보다 먼저 두고,
            // 방깎 후보끼리는 현재 패 제작 거리를 채용률보다 먼저 비교한다.
            .ThenByDescending(pair =>
                strategy is { ArmorBeforeSlow: true } &&
                !recipeLegendaryIds.Contains(pair.recommendation.Route.GoalUnitId) &&
                !IsActiveCommunityCore(pair.recommendation) &&
                GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(
                    pair.recommendation.Route.GoalUnitId)).ArmorReduction > 0 ? 1 : 0)
            .ThenByDescending(pair =>
                !recipeLegendaryIds.Contains(pair.recommendation.Route.GoalUnitId) &&
                GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(
                    pair.recommendation.Route.GoalUnitId)).ArmorReduction > 0
                    ? pair.recommendation.RecipeProgress.CompletionRatio
                    : 0)
            .ThenByDescending(pair =>
                ViviExpertPriority(
                    goal, pair.recommendation.Route.GoalUnitId,
                    viviPartnerId, goalOwned))
            .ThenByDescending(pair => pair.recommendation.RecipeProgress.CompletionRatio)
            .ThenBy(pair => pair.recommendation.RecipeProgress.MissingLeaves
                .Sum(leaf => leaf.MissingCount))
            .ThenByDescending(pair => strategy is { } supportStrategy
                ? RemainingUsefulMetricCount(GoalStrategyCalculator.StrategyMetricsFor(
                    catalog.Unit(pair.recommendation.Route.GoalUnitId)),
                    projectedBeforeSupports, supportStrategy)
                : 0)
            .ThenByDescending(pair =>
                CommunityPriorityScore(goal, catalog.Unit(pair.recommendation.Route.GoalUnitId)))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.recommendation)
            .ToList();

        var visibleGoal = showGoal ? [goalSuggestion] : Enumerable.Empty<Recommendation>();
        // 첫 희귀함 고정분은 기존 추천을 밀어내지 않도록 상한을 한 칸 늘려 허용한다.
        if (pinningTargetRare && rarePick is not null)
        {
            rarePick.RemainingCraftSteps.Clear();
            rarePick.RemainingCraftSteps.AddRange(goalSuggestion.RemainingCraftSteps);
            nearest = nearest
                .Where(item => !item.Route.GoalUnitId.Equals(
                    rarePick.Route.GoalUnitId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        var ordered = pinningTargetRare && rarePick is not null
            ? new[] { rarePick }.Concat(visibleGoal).Concat(nearest)
            : visibleGoal.Concat(nearest);
        var results = ordered
            .Take(Math.Max(1, effectiveTake +
                              (pinningFirstRareShip || pinningTargetRare ? 1 : 0)))
            .ToList();

        if (!seraphimBlocked &&
            strategy is not { StopAfterCoreTargets: true })
        {
            var bestSeraphim = catalog.AllUnits
                .Where(unit => unit.Tier.Split('[', 2)[0].Trim() == "세라핌")
                .DistinctBy(unit => unit.Id)
                .Where(unit => counts.GetValueOrDefault(unit.Id) <= 0)
                .Where(unit => MeetsOwnedPrerequisites(unit, counts))
                .Where(unit => GoalStrategyCalculator.IsCompatibleSupportDamageType(goal, unit))
                .Select(unit => (Unit: unit, Share: unit.Rawcodes
                    .Select(code => _activeClearProfile?.SupportShare.GetValueOrDefault(code) ?? 0)
                    .DefaultIfEmpty()
                    .Max(), Useful: strategy is { } seraphimStrategy
                        ? RemainingUsefulMetricCount(GoalStrategyCalculator.StrategyMetricsFor(unit),
                            projectedBeforeSupports, seraphimStrategy)
                        : 0))
                .Where(pair => pair.Useful > 0 || BuffSupportValue(pair.Unit) > 0 || pair.Share >= 0.10)
                .OrderByDescending(pair => EvaluateInitialCandidate(pair.Unit).RecipeProgress.CompletionRatio)
                .ThenByDescending(pair => pair.Useful)
                .ThenByDescending(pair => BuffSupportValue(pair.Unit))
                .ThenByDescending(pair => pair.Share)
                .FirstOrDefault();
            if (bestSeraphim.Unit is not null &&
                !results.Any(recommendation =>
                    BaseTier(catalog.Unit(recommendation.Route.GoalUnitId).Tier) ==
                    "세라핌"))
            {
                results.Add(EvaluateInitialCandidate(bestSeraphim.Unit));
            }
        }

        // 유저는 1번부터 순서대로 조합한다 — 위 순위 빌드가 소비할 패를 차감한 잔여
        // 패로 아래 순위의 완료율·남은 조합을 다시 계산해, 같은 카드가 여러 순위에
        // 이중 집계되지 않게 한다(1번 완료 시 2번 %가 부풀어 보이던 문제).
        // 세라핌은 초기 제한 뒤에 삽입될 수 있으므로 최종 목록에서도 take 계약을 지킨다.
        var protectedUnitIds = pinningTargetRare && rarePick is not null
            ? recipeLegendaryIds.Append(rarePick.Route.GoalUnitId).ToList()
            : recipeLegendaryIds;
        var preGateTake = navigation.AllowsMultipleTopUnits &&
                          !suppressSecondaryTopCandidates &&
                          !GoalStrategyCalculator.IsMagicDamageTier(goal.Tier)
            ? Math.Min(32, effectiveTake + 8)
            : navigation.AllowsMultipleTopUnits ? take : effectiveTake;
        results = LimitRecommendationsPreservingStrategy(
            results, preGateTake,
            goal, counts, strategy, showGoal, protectedUnitIds);

        IReadOnlyDictionary<string, int> cascadeInventory = counts;
        for (var i = 0; i < results.Count; i++)
        {
            results[i] = EvaluateCraft(catalog.Unit(results[i].Route.GoalUnitId),
                cascadeInventory, calculator, out var remainingAfterBuild);
            cascadeInventory = remainingAfterBuild;
            if (recipeLegendaryIds.Contains(results[i].Route.GoalUnitId))
                results[i].ClusterParentUnitId = goal.Id;
        }
        if (pinningTargetRare && rarePick is not null &&
            results.FirstOrDefault(item => item.Route.GoalUnitId.Equals(
                rarePick.Route.GoalUnitId, StringComparison.OrdinalIgnoreCase)) is { } progression)
        {
            progression.ProgressionGoalUnitId = goal.Id;
            var goalTier = BaseTier(goal.Tier);
            progression.ProgressionGoalName = goal.Name.Contains(
                goalTier, StringComparison.CurrentCulture)
                ? goal.Name
                : $"{goal.Name} {goalTier}";
        }

        foreach (var recommendation in results)
        {
            if (recommendation.Route.GoalUnitId.Equals(goalUnitId, StringComparison.OrdinalIgnoreCase))
            {
                if (IsViviEternal(goal))
                    recommendation.Warnings.Add(
                        "대깨 비영: 7강에서 멈춤 · 목재는 리롤 포함 20~28개까지만 사용");
                continue;
            }
            recommendation.ClearEvidence = BuildClearEvidence(
                catalog.Unit(recommendation.Route.GoalUnitId).Rawcodes);
        }
        var readinessInventory = counts.Select(pair => new InventoryEntry
        {
            UnitId = pair.Key,
            Count = pair.Value
        });
        var readiness = CombatReadinessCalculator.Calculate(
            catalog, goal, readinessInventory, difficulty);
        var carryMode = catalog.CarryPolicy.ForGoal(goal.Id).Mode;
        var requiredTopSupportIds = results
            .Where(item =>
                item.Route.GoalUnitId.Equals(viviPartnerId,
                    StringComparison.OrdinalIgnoreCase) ||
                GoalStrategyCalculator.IsMagicDamageTier(goal.Tier) &&
                GoalStrategyCalculator.StrategyMetricsFor(
                    catalog.Unit(item.Route.GoalUnitId))
                    .MagicArmorReduction > 0)
            .Select(item => item.Route.GoalUnitId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gate = SecondaryTopGate.Apply(
            results,
            goal.Id,
            carryMode,
            readiness,
            navigation.AllowsMultipleTopUnits ? take : effectiveTake,
            unitId => TopGradePolicy.IsTopGrade(catalog.Unit(unitId).Tier),
            requiredTopSupportIds);
        if (gate.DeferredCount > 0 &&
            !readiness.IsReady &&
            navigation.AllowsMultipleTopUnits &&
            gorosei != GoroseiMode.Warcury &&
            !suppressSecondaryTopCandidates)
        {
            var safe = RecommendNearestCrafts(
                goalUnitId,
                inventoryList,
                take,
                navigationMode,
                gorosei,
                buildVariant,
                suppressSeraphim,
                prioritizeTargetRare,
                suppressFirstRareShip,
                suppressSecondaryTopCandidates: true,
                difficulty: difficulty);
            foreach (var recommendation in safe)
            {
                recommendation.DeferredSecondaryTopCount =
                    Math.Max(gate.DeferredCount,
                        recommendation.DeferredSecondaryTopCount);
                recommendation.DeferredSecondaryTopReason =
                    "55라 준비 미달 — 2상위 보류";
            }
            return safe;
        }
        foreach (var recommendation in gate.Recommendations)
        {
            recommendation.CombatReadiness = readiness;
            recommendation.CarryMode = carryMode;
            recommendation.DeferredSecondaryTopCount = gate.DeferredCount;
            recommendation.DeferredSecondaryTopReason = gate.DeferredReason;
        }
        return gate.Recommendations;

        Recommendation EvaluateInitialCandidate(UnitDefinition unit)
        {
            if (initialCandidateEvaluations.TryGetValue(unit.Id, out var cached)) return cached;
            if (!_candidateProgressCache.TryGetValue(unit.Id, out var progress))
            {
                progress = calculator.Calculate([unit.Id], counts);
                _candidateProgressCache[unit.Id] = progress;
            }
            var evaluated = EvaluateCraftCandidate(unit, progress);
            initialCandidateEvaluations[unit.Id] = evaluated;
            return evaluated;
        }
    }

    private void PrepareCandidateProgressCache(IReadOnlyDictionary<string, int> inventory)
    {
        if (_candidateProgressInventory.Count == inventory.Count &&
            inventory.All(pair =>
                _candidateProgressInventory.GetValueOrDefault(pair.Key) == pair.Value))
            return;
        _candidateProgressInventory = new Dictionary<string, int>(
            inventory, StringComparer.OrdinalIgnoreCase);
        _candidateProgressCache.Clear();
    }

    /// <summary>
    /// 후보 보드에서 고른 칸이 재료를 먼저 쓰도록 완료율을 다시 계산한다.
    /// 칸 순서는 그대로 두고, 퍼센트만 선택한 패 기준으로 바꾼다.
    /// </summary>
    public IReadOnlyList<Recommendation> Recascade(
        IReadOnlyList<Recommendation> recs,
        IEnumerable<InventoryEntry> inventory,
        string? consumeFirstRouteId)
    {
        if (recs.Count == 0) return recs;
        var counts = inventory
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var calculator = _recipeCalculator;
        var selected = recs.FirstOrDefault(item =>
            item.Route.Id.Equals(consumeFirstRouteId, StringComparison.OrdinalIgnoreCase));
        var consumeOrder = selected is null
            ? recs
            : new[] { selected }.Concat(recs.Where(item =>
                !item.Route.Id.Equals(selected.Route.Id, StringComparison.OrdinalIgnoreCase)));
        var remaining = counts;
        var rebuilt = new Dictionary<string, Recommendation>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in consumeOrder)
        {
            var crafted = EvaluateCraft(catalog.Unit(rec.Route.GoalUnitId), remaining, calculator,
                out var leftover);
            remaining = leftover;
            crafted.ClusterParentUnitId = rec.ClusterParentUnitId;
            crafted.ClearEvidence = rec.ClearEvidence;
            if (rec.ProgressionGoalUnitId is { Length: > 0 } progressionGoalId)
            {
                crafted.ProgressionGoalUnitId = progressionGoalId;
                crafted.ProgressionGoalName = rec.ProgressionGoalName ??
                                              catalog.Unit(progressionGoalId).Name;
            }
            rebuilt[rec.Route.Id] = crafted;
        }
        return recs.Select(item => rebuilt[item.Route.Id]).ToList();
    }

    /// <summary>
    /// 자동 시작 단계: 현재 패로 가장 빨리 완성되는 희귀함 순위. 서로 대안 관계라
    /// 각 희귀함은 전체 패 기준으로 독립 평가한다(순위 캐스케이드 미적용).
    /// 빈 패에서는 재료 수가 적은(빨리 나오는) 순서가 된다.
    /// </summary>
    public IReadOnlyList<Recommendation> RecommendFastRares(
        IEnumerable<InventoryEntry> inventory, int take = 5)
    {
        var counts = inventory
            .GroupBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Count),
                StringComparer.OrdinalIgnoreCase);
        var calculator = _recipeCalculator;
        var rares = catalog.AllUnits
            .Where(unit => unit.Tier.Split('[', 2)[0].Trim() == "희귀함")
            .Where(unit => unit.Recipe.Count > 0)
            .DistinctBy(unit => unit.Id)
            .Select(unit => EvaluateCraft(unit, counts, calculator))
            .Where(recommendation => recommendation.RecipeProgress.RequiredLeafCount > 0)
            .OrderByDescending(recommendation => recommendation.RecipeProgress.CompletionRatio)
            .ThenBy(recommendation => recommendation.RecipeProgress.RequiredLeafCount)
            .Take(Math.Max(1, take))
            .ToList();
        return rares;
    }

    /// <summary>
    /// 선택한 후보의 바로 아래 조합 재료. 레시피에서 가장 높은 등급 묶음만 붙인다.
    /// 세라핌→전설/히든, 초월→전설급, 전설/히든→희귀함(호스트가 전설이면 전설),
    /// 희귀함→특별함. 후보 보드 클러스터는 지금 고른 칸에 붙인다.
    /// </summary>
    public IReadOnlyList<Recommendation> StoryClusterChildren(
        string unitId, IEnumerable<InventoryEntry> inventory)
    {
        var counts = inventory
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var unit = catalog.Unit(unitId);
        var calculator = _recipeCalculator;
        return StoryClusterChildIds(unit)
            .Where(id => counts.GetValueOrDefault(id) <= 0)
            .Select(id =>
            {
                var child = EvaluateCraft(catalog.Unit(id), counts, calculator);
                child.ClusterParentUnitId = unit.Id;
                return child;
            })
            .ToList();
    }

    private List<string> StoryClusterChildIds(UnitDefinition root)
    {
        var materials = root.Recipe.Keys
            .Select(catalog.Unit)
            .Where(unit => ClusterBand(unit.Tier) > 0)
            .ToList();
        if (materials.Count == 0) return [];
        var top = materials.Max(unit => ClusterBand(unit.Tier));
        return materials
            .Where(unit => ClusterBand(unit.Tier) == top)
            .Select(unit => unit.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 보드에 묶을 재료 등급. 높을수록 우선. 0은 흔함·아이템처럼 클러스터에서 제외.
    /// </summary>
    private static int ClusterBand(string tier) => BaseTier(tier) switch
    {
        "초월" or "불멸" or "영원" or "제한됨" or "신비함" => 5,
        "전설" or "히든" or "변화된" or "왜곡됨" or "세라핌" => 4,
        "해적선" or "함선" => 3,
        "희귀함" => 2,
        "특별함" => 1,
        _ => 0
    };

    // 배(해적선 060h·고대의 배 Y50h)는 일반 재료 조합으로 만들 수 없는 특수 획득물이다.
    // 좀비·토큰·확장팩·초월쿠마 같은 기타 재료는 게임 안에서 정상 획득 루트가 있으므로
    // 게이트하지 않는다(전부 게이트하면 초월 후보가 통째로 사라진다).
    private static readonly string[] ShipPrerequisiteRawcodes = ["060h", "Y50h"];

    /// <summary>
    /// 레시피 트리가 배·아이템을 요구하는 유닛은 그 재료가 실제 패에 있을 때만
    /// 지원 후보로 노출한다. 중간 재료를 이미 보유했다면 그 하위 트리는 따지지 않는다.
    /// 특포(POINT)는 패로 안 잡혀 게이트하지 않고, 카드 경고만 띄운다.
    /// </summary>
    private bool MeetsOwnedPrerequisites(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory) =>
        SpecialRequirementSatisfied(unit, inventory,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// 조합에 특포가 들어가는 상위(알비다 제한 등)는 첫 상위 특강 예산을 빼고
    /// 남는 특포가 있을 때만 추천한다. 첫 상위는 거의 특강을 하므로, 특성공학이
    /// 아니고 특포 잔량이 안 보이면 추천하지 않는다.
    /// </summary>
    private bool AvoidTraitPointCraftWithoutEconomy(
        NavigationOption navigation, UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory)
    {
        var needed = TraitPointCost(unit);
        if (needed <= 0) return false;
        if (navigation.Id.Equals("AlliedForces.TraitEngineering",
                StringComparison.OrdinalIgnoreCase))
            return false;
        return OwnedTraitPoints(inventory) - FirstTopTraitEnhanceCost < needed;
    }

    private int TraitPointCost(UnitDefinition unit)
    {
        var total = 0;
        foreach (var (childId, required) in unit.Recipe)
        {
            var child = catalog.Unit(childId);
            if (child.Rawcodes.Contains("POINT", StringComparer.Ordinal) ||
                childId.Equals("POINT", StringComparison.OrdinalIgnoreCase) ||
                childId.EndsWith(":POINT", StringComparison.OrdinalIgnoreCase))
                total += required;
        }
        return total;
    }

    private static int OwnedTraitPoints(IReadOnlyDictionary<string, int> inventory) =>
        Math.Max(inventory.GetValueOrDefault("POINT"),
            inventory.GetValueOrDefault("rawcode:POINT"));

    private const int FirstTopTraitEnhanceCost = 4;

    private static bool IsSpecialPrerequisite(UnitDefinition unit) =>
        unit.Rawcodes.Any(code =>
            ShipPrerequisiteRawcodes.Contains(code, StringComparer.Ordinal)) ||
        BaseTier(unit.Tier) == "아이템";

    private bool SpecialRequirementSatisfied(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory, HashSet<string> visiting)
    {
        if (!visiting.Add(unit.Id)) return true;
        foreach (var (childId, required) in unit.Recipe)
        {
            var child = catalog.Unit(childId);
            if (IsResourcePseudo(child)) continue;
            var owned = Math.Max(inventory.GetValueOrDefault(child.Id),
                inventory.GetValueOrDefault(childId));
            if (owned >= required) continue;
            if (IsSpecialPrerequisite(child)) return false;
            if (!SpecialRequirementSatisfied(child, inventory, visiting)) return false;
        }
        return true;
    }

    private List<string> CollectMissingSpecials(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory)
    {
        var missing = new List<string>();
        Walk(unit, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return missing.Distinct(StringComparer.CurrentCulture).ToList();

        void Walk(UnitDefinition current, HashSet<string> visiting)
        {
            if (!visiting.Add(current.Id)) return;
            foreach (var (childId, required) in current.Recipe)
            {
                var child = catalog.Unit(childId);
                if (child.Rawcodes.Contains("POINT", StringComparer.Ordinal))
                {
                    if (inventory.GetValueOrDefault(child.Id) < required)
                        missing.Add($"특성포인트 {required}개");
                    continue;
                }
                if (IsResourcePseudo(child)) continue;
                var owned = Math.Max(inventory.GetValueOrDefault(child.Id),
                    inventory.GetValueOrDefault(childId));
                if (owned >= required) continue;
                if (IsSpecialPrerequisite(child))
                {
                    var missingCount = required - owned;
                    missing.Add(missingCount > 1
                        ? $"{child.Name} ×{missingCount}"
                        : child.Name);
                }
                else
                    Walk(child, visiting);
            }
        }
    }

    // 후보의 조합 경로가 소비해야 하는 배 코드 목록(보유한 중간재 하위는 제외).
    // 한 패스 안에서 인벤토리가 고정이므로 유닛별로 캐시한다.
    private readonly Dictionary<string, List<string>> _shipNeedCache =
        new(StringComparer.OrdinalIgnoreCase);

    internal List<string> RequiredShipCodes(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory)
    {
        if (_shipNeedCache.TryGetValue(unit.Id, out var cached)) return cached;
        var needed = new List<string>();
        var availability = inventory
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        Walk(unit, 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _shipNeedCache[unit.Id] = needed;
        return needed;

        void Walk(UnitDefinition current, int multiplier, HashSet<string> visiting)
        {
            if (!visiting.Add(current.Id)) return;
            foreach (var (childId, required) in current.Recipe)
            {
                var child = catalog.Unit(childId);
                if (IsResourcePseudo(child)) continue;
                var totalRequired = required > int.MaxValue / Math.Max(1, multiplier)
                    ? int.MaxValue
                    : required * multiplier;
                // 배 재료는 보유 여부와 무관하게 소요로 센다 — 보유한 배가 바로
                // 이 후보가 소모할 자원이다. 배가 아닌 보유 중간재는 이미 완성돼
                // 있으므로(그 배도 그때 소모됨) 하위를 더 세지 않는다.
                var ship = child.Rawcodes.FirstOrDefault(code =>
                    ShipPrerequisiteRawcodes.Contains(code, StringComparer.Ordinal));
                if (ship is not null)
                {
                    for (var count = 0; count < totalRequired; count++)
                        needed.Add(ship);
                    continue;
                }
                var owned = Math.Min(totalRequired,
                    availability.GetValueOrDefault(child.Id));
                if (owned > 0) availability[child.Id] -= owned;
                var remaining = totalRequired - owned;
                if (remaining > 0) Walk(child, remaining, visiting);
            }
            visiting.Remove(current.Id);
        }
    }

    // 물딜·마딜 공용 역할 파이프라인. 스턴 1.4 축은 공통이고, 마감 깎기만
    // 물딜(방깎 211)과 마딜(마방깎 소스 1)로 갈린다.
    private bool MeetsCoreTargets(UnitDefinition goal,
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyCollection<Recommendation> supports,
        bool includeGoal,
        GoalStrategyProfile strategy)
    {
        var projected = AggregateStrategyMetrics(inventory);
        if (includeGoal) projected += GoalStrategyCalculator.StrategyMetricsFor(goal);
        foreach (var support in supports)
            projected += GoalStrategyCalculator.StrategyMetricsFor(
                catalog.Unit(support.Route.GoalUnitId));
        return MeetsStrategyTargets(projected, strategy);
    }

    private static bool MeetsStrategyTargets(StrategyMetrics projected,
        GoalStrategyProfile strategy) =>
        projected.Slow + 0.0001 >= strategy.SlowTarget &&
               projected.Stun + 0.0001 >= strategy.StunTarget &&
               projected.ArmorReduction + 0.0001 >= strategy.ArmorReductionTarget &&
               projected.ArmorBreak + 0.0001 >= strategy.ArmorBreakTarget &&
               projected.AirMovement + 0.0001 >= strategy.AirMovementTarget &&
               projected.BossControl + 0.0001 >= strategy.BossControlTarget &&
               projected.BerserkBossControl + 0.0001 >= strategy.BerserkBossControlTarget &&
               projected.MagicArmorReduction + 0.0001 >= strategy.MagicArmorReductionTarget &&
               projected.SingleDamage + 0.0001 >= strategy.SingleDamageTarget &&
               projected.FinisherDamage + 0.0001 >= strategy.FinisherDamageTarget;

    private List<Recommendation> OrderStrategySupports(UnitDefinition goal,
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyCollection<CraftCandidate> candidates,
        int take,
        GoalStrategyProfile strategy,
        bool allowsMultipleTopUnits,
        bool canCraftGoal)
    {
        var selected = new List<CraftCandidate>();
        // 마딜 파이프라인이 활성인 동안은 공증·공속 버필러(쵸파 혼포인트 등)가
        // 역할 수치·커뮤니티 점수와 무관하게 버프 슬롯 후보로 남아야 한다.
        var includeBuffFillers = strategy.PreferCheapStatFillers &&
                                 strategy.ArmorReductionTarget <= 0;
        var remaining = candidates
            .Where(candidate => candidate.Metrics.HasAny ||
                                includeBuffFillers && IsCheapBuffFiller(candidate.Unit) ||
                                strategy.FillCommunitySupports &&
                                (BuffSupportValue(candidate.Unit) > 0 ||
                                 CommunityPriorityScore(goal, candidate.Unit) > 0))
            .ToList();
        if (strategy.CommunityCoreTarget > 0)
        {
            var availableCoreCount = remaining.Count(candidate =>
                IsCommunityCore(goal, candidate.Unit, strategy));
            strategy = strategy with
            {
                CommunityCoreTarget = Math.Min(
                    strategy.CommunityCoreTarget, availableCoreCount)
            };
        }
        var projected = AggregateStrategyMetrics(inventory);
        if (canCraftGoal && inventory.GetValueOrDefault(goal.Id) <= 0)
            projected += GoalStrategyCalculator.StrategyMetricsFor(goal);
        var armorWasPendingAtStart =
            projected.ArmorReduction + 0.0001 < strategy.ArmorReductionTarget;

        // 마딜은 짤깍 칸 대신 쵸파 희귀 같은 버퍼 1기를 남긴다.
        var magicBuffPending = strategy.PreferCheapStatFillers &&
                               strategy.ArmorReductionTarget <= 0 &&
                               !inventory.Any(pair => pair.Value > 0 &&
                                                      IsCheapBuffFiller(catalog.Unit(pair.Key)));
        var roleCap = Math.Max(0, take - (magicBuffPending ? 1 : 0));

        void SelectArmorSet()
        {
            if (strategy.MinimizeArmorRecommendationSet && selected.Count < roleCap &&
                projected.ArmorReduction + 0.0001 < strategy.ArmorReductionTarget)
            {
                var armorSet = ChooseArmorSet(goal, strategy, projected, remaining, selected,
                    inventory, roleCap - selected.Count);
                foreach (var candidate in armorSet)
                    Add(candidate);
            }
            else while (selected.Count < roleCap &&
                        projected.ArmorReduction + 0.0001 < strategy.ArmorReductionTarget)
            {
                var armorPool = remaining
                        .Where(candidate => candidate.Metrics.ArmorReduction > 0)
                        .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected,
                            inventory, projected, strategy))
                        .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap));
                var armor = OrderTowardsTarget(
                        PreferCheapFillers(armorPool, IsCheapArmorFiller, BigMetricCount(
                            metrics => metrics.ArmorReduction, IsCheapArmorFiller), 3),
                        projected.ArmorReduction, strategy.ArmorReductionTarget,
                        candidate => candidate.Metrics.ArmorReduction, projected, strategy, goal)
                    .FirstOrDefault();
                if (armor is null) break;
                Add(armor);
            }
        }

        // 모든 물딜은 상위별 시너지보다 먼저 스턴 1.4를 확보해 라인을 안정시킨다.
        if (strategy.StunBeforeSlow && selected.Count < roleCap &&
            projected.Stun + 0.0001 < strategy.StunTarget)
        {
            var stunSet = ChooseStunSet(goal, strategy, projected, remaining, selected, inventory,
                roleCap - selected.Count);
            foreach (var candidate in stunSet)
                Add(candidate);
        }

        // 조초의 크제/봉히처럼 전용 파트너가 스턴 조합에 이미 포함되지 않았다면
        // 다음 순서에서 한 기만 보완한다.
        while (selected.Count < roleCap &&
               selected.Count(candidate => IsCommunityCore(goal, candidate.Unit, strategy)) <
               strategy.CommunityCoreTarget)
        {
            var core = remaining
                .Where(candidate => IsCommunityCore(goal, candidate.Unit, strategy))
                .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap))
                .OrderByDescending(candidate => candidate.Recommendation.RecipeProgress.CompletionRatio)
                .ThenBy(candidate => candidate.Recommendation.RecipeProgress.MissingLeaves
                    .Sum(leaf => leaf.MissingCount))
                .ThenByDescending(candidate => CommunityPriorityScore(goal, candidate.Unit))
                .FirstOrDefault();
            if (core is null) break;
            Add(core);
        }

        // 징초는 자체 암브 외에 한 기가 더 있어야 한다는 최신 공략을 별도 목표로
        // 둔다. 아머브레이크를 방깎 수치로 환산하지 않고 기수로만 센다.
        while (selected.Count < roleCap && projected.ArmorBreak + 0.0001 < strategy.ArmorBreakTarget)
        {
            var armorBreak = OrderTowardsTarget(remaining
                    .Where(candidate => candidate.Metrics.ArmorBreak > 0)
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap)), projected.ArmorBreak,
                strategy.ArmorBreakTarget, candidate => candidate.Metrics.ArmorBreak,
                projected, strategy, goal)
                .FirstOrDefault();
            if (armorBreak is null) break;
            Add(armorBreak);
        }

        if (strategy.ArmorBeforeSlow) SelectArmorSet();

        while (selected.Count < roleCap && projected.Slow + 0.0001 < strategy.SlowTarget)
        {
            var slowPool = remaining
                    .Where(candidate => candidate.Metrics.Slow > 0)
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap));
            var next = OrderTowardsTarget(
                    PreferCheapFillers(slowPool, IsCheapSlowFiller, BigMetricCount(
                        metrics => metrics.Slow, IsCheapSlowFiller), 2),
                    projected.Slow, strategy.SlowTarget, candidate => candidate.Metrics.Slow,
                    projected, strategy, goal)
                .FirstOrDefault();
            if (next is null) break;
            Add(next);
        }

        if (!strategy.StunBeforeSlow && selected.Count < roleCap &&
            projected.Stun + 0.0001 < strategy.StunTarget)
        {
            var stunSet = ChooseStunSet(goal, strategy, projected, remaining, selected, inventory,
                roleCap - selected.Count);
            foreach (var candidate in stunSet)
                Add(candidate);
        }

        // 크립·정의의 문·해왕류·황금종 동선을 위해 공중이동 한 기를 확보한다.
        // 별도 유틸 전설을 낭비하지 않고, 마감 깎기(물딜은 방깎, 마딜은 마방깎)를
        // 짜는 단계에서 공중이동을 함께 제공하는 후보를 먼저 고른다. 그런 후보가
        // 없을 때만 다른 유효 역할을 겸하는 공중이동 후보로 폴백한다.
        var finisherIsMagic = strategy.ArmorReductionTarget <= 0 &&
                              strategy.MagicArmorReductionTarget > 0;
        while (selected.Count < roleCap &&
               projected.AirMovement + 0.0001 < strategy.AirMovementTarget)
        {
            var compatibleAir = remaining
                .Where(candidate => candidate.Metrics.AirMovement > 0)
                .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap))
                .ToList();
            Func<CraftCandidate, double> finisher = finisherIsMagic
                ? candidate => candidate.Metrics.MagicArmorReduction
                : candidate => candidate.Metrics.ArmorReduction;
            var air = OrderTowardsTarget(
                    compatibleAir.Any(candidate => finisher(candidate) > 0)
                        ? compatibleAir.Where(candidate => finisher(candidate) > 0)
                        : compatibleAir,
                    finisherIsMagic ? projected.MagicArmorReduction : projected.ArmorReduction,
                    finisherIsMagic ? strategy.MagicArmorReductionTarget : strategy.ArmorReductionTarget,
                    finisher,
                    projected, strategy, goal)
                .FirstOrDefault();
            if (air is null) break;
            Add(air);
        }

        if (!strategy.ArmorBeforeSlow) SelectArmorSet();

        while (selected.Count < roleCap &&
               projected.BossControl + 0.0001 < strategy.BossControlTarget)
        {
            var boss = OrderTowardsTarget(remaining
                    .Where(candidate => candidate.Metrics.BossControl > 0)
                    .Where(candidate => IsCompatibleSupport(
                        goal, candidate.Unit, selected, inventory, projected, strategy))
                    .Where(candidate => FitsStunCap(
                        projected, candidate, strategy.StunCap)),
                projected.BossControl, strategy.BossControlTarget,
                candidate => candidate.Metrics.BossControl,
                projected, strategy, goal)
                .FirstOrDefault();
            if (boss is null) break;
            Add(boss);
        }

        while (selected.Count < roleCap &&
               projected.BerserkBossControl + 0.0001 <
               strategy.BerserkBossControlTarget)
        {
            var berserk = OrderTowardsTarget(remaining
                    .Where(candidate => candidate.Metrics.BerserkBossControl > 0)
                    .Where(candidate => IsCompatibleSupport(
                        goal, candidate.Unit, selected, inventory, projected, strategy))
                    .Where(candidate => FitsStunCap(
                        projected, candidate, strategy.StunCap)),
                projected.BerserkBossControl,
                strategy.BerserkBossControlTarget,
                candidate => candidate.Metrics.BerserkBossControl,
                projected, strategy, goal)
                .FirstOrDefault();
            if (berserk is null) break;
            Add(berserk);
        }

        // 마딜 상위의 마감 깎기: 마방깎 소스를 최소 목표만큼 확보한다. 물딜의 방깎
        // 211과 달리 큰 수치 목표를 두지 않는다(실측상 보편 스택이 아님).
        while (selected.Count < roleCap &&
               projected.MagicArmorReduction + 0.0001 < strategy.MagicArmorReductionTarget)
        {
            var magicArmor = OrderTowardsTarget(remaining
                    .Where(candidate => candidate.Metrics.MagicArmorReduction > 0)
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap)),
                projected.MagicArmorReduction,
                strategy.MagicArmorReductionTarget,
                candidate => candidate.Metrics.MagicArmorReduction,
                projected, strategy, goal)
                .FirstOrDefault();
            if (magicArmor is null) break;
            Add(magicArmor);
        }

        // 딜 밸런스(고인물 검증): 필수 유틸을 채운 뒤 남는 슬롯에서, 상위가 단일이면
        // 끝딜 한 기, 끝딜이면 단일 한 기를 보완한다. 패에 이미 있으면 건너뛴다.
        while (selected.Count < roleCap &&
               projected.FinisherDamage + 0.0001 < strategy.FinisherDamageTarget)
        {
            var finisher = OrderTowardsTarget(remaining
                    .Where(candidate => candidate.Metrics.FinisherDamage > 0)
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap)),
                projected.FinisherDamage, strategy.FinisherDamageTarget,
                candidate => candidate.Metrics.FinisherDamage, projected, strategy, goal)
                .FirstOrDefault();
            if (finisher is null) break;
            Add(finisher);
        }

        while (selected.Count < roleCap &&
               projected.SingleDamage + 0.0001 < strategy.SingleDamageTarget)
        {
            var single = OrderTowardsTarget(remaining
                    .Where(candidate => candidate.Metrics.SingleDamage > 0)
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap)),
                projected.SingleDamage, strategy.SingleDamageTarget,
                candidate => candidate.Metrics.SingleDamage, projected, strategy, goal)
                .FirstOrDefault();
            if (single is null) break;
            Add(single);
        }

        if (strategy.OptionalBossSupportAfterCore && selected.Count < roleCap &&
            projected.BossControl <= 0 && projected.BerserkBossControl <= 0)
        {
            var optionalBoss = OrderByCraftDistance(remaining
                    .Where(candidate => candidate.Metrics.BossControl > 0 ||
                                        candidate.Metrics.BerserkBossControl > 0)
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap)))
                .FirstOrDefault();
            if (optionalBoss is not null) Add(optionalBoss);
        }

        // 마딜은 짤깍이 필요 없다. 남는 한 칸은 쵸파 혼포인트 같은 공증·공속 버퍼.
        if (strategy.PreferCheapStatFillers && strategy.ArmorReductionTarget <= 0 &&
            selected.Count < take &&
            !selected.Any(candidate => IsCheapBuffFiller(candidate.Unit)) &&
            !inventory.Any(pair => pair.Value > 0 && IsCheapBuffFiller(catalog.Unit(pair.Key))))
        {
            var buff = remaining
                .Where(candidate => IsCheapBuffFiller(candidate.Unit))
                .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap))
                .OrderByDescending(candidate =>
                    GoalStrategyCalculator.AbilityTotal(candidate.Unit, "공격력 증가") +
                    GoalStrategyCalculator.AbilityTotal(candidate.Unit, "공격속도 증가"))
                .ThenByDescending(candidate =>
                    candidate.Recommendation.RecipeProgress.CompletionRatio)
                .ThenByDescending(candidate => CommunityPriorityScore(goal, candidate.Unit))
                .FirstOrDefault();
            if (buff is not null) Add(buff);
        }

        while (strategy.FillCommunitySupports && !strategy.StopAfterCoreTargets &&
               selected.Count < take)
        {
            var support = remaining
                .Where(candidate => BuffSupportValue(candidate.Unit) > 0 ||
                                    CommunityPriorityScore(goal, candidate.Unit) > 0)
                .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, strategy))
                .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap))
                .OrderByDescending(candidate =>
                    candidate.Recommendation.RecipeProgress.CompletionRatio)
                .ThenBy(candidate => candidate.Recommendation.RecipeProgress.MissingLeaves
                    .Sum(leaf => leaf.MissingCount))
                .ThenByDescending(candidate => BuffSupportValue(candidate.Unit))
                .ThenByDescending(candidate => CommunityPriorityScore(goal, candidate.Unit))
                .FirstOrDefault();
            if (support is null) break;
            Add(support);
        }

        // 다상위 항법에서만 핵심 수치 완성 뒤의 추가 상위 후보를 이어서 보여준다.
        // 패왕의길에서는 불필요한 방깎/보잡을 목표치 이상으로 억지 추천하지 않는다.
        while (allowsMultipleTopUnits && selected.Count < take)
        {
            var upper = OrderByCraftDistance(remaining
                    .Where(candidate => IsTopTier(candidate.Unit.Tier))
                    .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                        projected, strategy))
                    .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap)))
                .FirstOrDefault();
            if (upper is null) break;
            Add(upper);
        }

        if (strategy.StopAfterCoreTargets && !allowsMultipleTopUnits)
        {
            selected = OptimizeCoreSetForCraftDistance(goal, inventory, candidates,
                selected, strategy, canCraftGoal);
            projected = AggregateStrategyMetrics(inventory);
            if (canCraftGoal && inventory.GetValueOrDefault(goal.Id) <= 0)
                projected += GoalStrategyCalculator.StrategyMetricsFor(goal);
            foreach (var candidate in selected) projected += candidate.Metrics;

            // 핵심 목표를 유지하면서 중복 후보를 지울 때는 현재 패에서 먼 기물부터
            // 제거한다. 가까운 방깎을 꼬리 삽입됐다는 이유로 다시 버리지 않는다.
            foreach (var candidate in selected
                         .OrderByDescending(item => item.Recommendation.RecipeProgress.MissingLeaves
                             .Sum(leaf => leaf.MissingCount))
                         .ThenBy(item => item.Recommendation.RecipeProgress.CompletionRatio)
                         .ToList())
            {
                var candidateMissing = candidate.Recommendation.RecipeProgress.MissingLeaves
                    .Sum(leaf => leaf.MissingCount);
                var hasFartherArmor = candidate.Metrics.ArmorReduction > 0 &&
                    armorWasPendingAtStart &&
                    candidateMissing <= 2 &&
                    selected.Any(other => !ReferenceEquals(other, candidate) &&
                        other.Metrics.ArmorReduction > 0 &&
                        (other.Recommendation.RecipeProgress.CompletionRatio + 0.0001 <
                         candidate.Recommendation.RecipeProgress.CompletionRatio ||
                         other.Recommendation.RecipeProgress.MissingLeaves
                             .Sum(leaf => leaf.MissingCount) > candidateMissing));
                if (hasFartherArmor) continue;
                var trial = projected + candidate.Metrics * -1;
                if (!MeetsStrategyTargets(trial, strategy)) continue;
                projected = trial;
                selected.Remove(candidate);
            }
        }

        return selected.Select(candidate => candidate.Recommendation).ToList();

        void Add(CraftCandidate candidate)
        {
            selected.Add(candidate);
            remaining.Remove(candidate);
            projected += candidate.Metrics;
        }

        // 거프처럼 풀이감·풀깎을 전설로만 채우면 짤이감·짤깍이 밀린다.
        // 큰 소스 2~3기를 고른 뒤에는 희귀/특별 필러로 숫자를 맞춘다.
        IEnumerable<CraftCandidate> PreferCheapFillers(
            IEnumerable<CraftCandidate> pool,
            Func<UnitDefinition, bool> isCheap,
            int bigCount,
            int bigLimit)
        {
            if (!strategy.PreferCheapStatFillers || bigCount < bigLimit) return pool;
            // 짤필러는 역할당 1기만 강제한다. 이감 20짜리 희귀함으로 102를 채우면
            // 단일·끝딜·보잡 자리가 없어진다.
            if (selected.Count(candidate => isCheap(candidate.Unit)) >= 1) return pool;
            var cheap = pool.Where(candidate => isCheap(candidate.Unit)).ToList();
            return cheap.Count > 0 ? cheap : pool;
        }

        int BigMetricCount(Func<StrategyMetrics, double> metric, Func<UnitDefinition, bool> isCheap)
        {
            var fromSelected = selected.Count(candidate =>
                metric(candidate.Metrics) > 0 && !isCheap(candidate.Unit));
            var fromOwned = inventory
                .Where(pair => pair.Value > 0)
                .Select(pair => catalog.Unit(pair.Key))
                .Where(unit => CountsAsCompletedSupport(unit) && !isCheap(unit))
                .Count(unit => metric(GoalStrategyCalculator.StrategyMetricsFor(unit)) > 0);
            return fromSelected + fromOwned;
        }
    }

    private List<CraftCandidate> OptimizeCoreSetForCraftDistance(UnitDefinition goal,
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyCollection<CraftCandidate> candidates,
        List<CraftCandidate> selected,
        GoalStrategyProfile strategy,
        bool canCraftGoal)
    {
        var baseline = AggregateStrategyMetrics(inventory);
        if (canCraftGoal && inventory.GetValueOrDefault(goal.Id) <= 0)
            baseline += GoalStrategyCalculator.StrategyMetricsFor(goal);
        if (baseline.ArmorReduction + 0.0001 >= strategy.ArmorReductionTarget)
            return selected;

        StrategyMetrics Metrics(IReadOnlyCollection<CraftCandidate> plan)
        {
            var metrics = baseline;
            foreach (var candidate in plan) metrics += candidate.Metrics;
            return metrics;
        }

        var best = selected.ToList();
        var bestMeetsTargets = MeetsStrategyTargets(Metrics(best), strategy);
        var maximumRoleCap = 12 - (canCraftGoal &&
                                   inventory.GetValueOrDefault(goal.Id) <= 0 ? 1 : 0);
        var alternatives = OrderByCraftDistance(candidates
                .Where(candidate => candidate.Metrics.ArmorReduction > 0)
                .Where(candidate => MissingLeafCost(candidate) <= 2))
            .Take(18)
            .ToList();
        foreach (var alternative in alternatives)
        {
            if (best.Any(candidate => candidate.Unit.Id.Equals(alternative.Unit.Id,
                    StringComparison.OrdinalIgnoreCase)))
                continue;
            var fartherArmor = best
                .Where(candidate => candidate.Metrics.ArmorReduction > 0)
                .Where(candidate => IsCloser(alternative, candidate))
                .ToList();
            if (fartherArmor.Count == 0) continue;

            var trial = best.Append(alternative)
                .DistinctBy(candidate => candidate.Unit.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            while (trial.Count > maximumRoleCap)
            {
                var removable = fartherArmor
                    .Where(candidate => trial.Contains(candidate))
                    .OrderByDescending(MissingLeafCost)
                    .ThenBy(candidate =>
                        candidate.Recommendation.RecipeProgress.CompletionRatio)
                    .FirstOrDefault(candidate =>
                    {
                        var without = trial.Where(item => !ReferenceEquals(item, candidate))
                            .ToList();
                        return MeetsStrategyTargets(Metrics(without), strategy) &&
                               IsCompatiblePlan(without);
                    });
                if (removable is null) break;
                trial.Remove(removable);
            }
            if (trial.Count <= maximumRoleCap && IsCompatiblePlan(trial) &&
                (!bestMeetsTargets || MeetsStrategyTargets(Metrics(trial), strategy)))
            {
                best = trial;
                break;
            }
        }
        return best;

        bool IsCompatiblePlan(IReadOnlyCollection<CraftCandidate> plan)
        {
            var accepted = new List<CraftCandidate>();
            var projected = baseline;
            foreach (var candidate in plan)
            {
                if (!IsCompatibleSupport(goal, candidate.Unit, accepted, inventory,
                        projected, strategy))
                    return false;
                accepted.Add(candidate);
                projected += candidate.Metrics;
            }
            return projected.Stun <= strategy.StunCap + 0.0001;
        }

        static long MissingLeafCost(CraftCandidate candidate) =>
            candidate.Recommendation.RecipeProgress.MissingLeaves
                .Sum(leaf => (long)leaf.MissingCount);

        static bool IsCloser(CraftCandidate candidate, CraftCandidate current)
        {
            var candidateCompletion =
                candidate.Recommendation.RecipeProgress.CompletionRatio;
            var currentCompletion = current.Recommendation.RecipeProgress.CompletionRatio;
            return candidateCompletion > currentCompletion + 0.0001 ||
                   Math.Abs(candidateCompletion - currentCompletion) < 0.0001 &&
                   MissingLeafCost(candidate) < MissingLeafCost(current);
        }
    }

    private List<CraftCandidate> ChooseStunSet(UnitDefinition goal,
        GoalStrategyProfile strategy,
        StrategyMetrics projected,
        IReadOnlyCollection<CraftCandidate> remaining,
        IReadOnlyCollection<CraftCandidate> alreadySelected,
        IReadOnlyDictionary<string, int> inventory,
        int availableSlots)
    {
        if (availableSlots <= 0) return [];
        var pool = OrderByCraftDistance(remaining
                .Where(candidate => candidate.Metrics.Stun > 0)
                .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, alreadySelected, inventory,
                    projected, strategy)))
            .Take(24)
            .ToList();
        var maximumPicks = Math.Min(3, availableSlots);
        List<CraftCandidate>? best = null;
        var bestReachesTarget = false;
        var bestDistance = double.MaxValue;
        var bestCoreCoverage = -1;
        var bestSize = int.MaxValue;
        var bestUsefulMetrics = -1;
        var bestOvershoot = true;
        var bestCraftScore = (Completion: double.MinValue, MissingLeaves: long.MinValue,
            CommunityPriority: int.MinValue);
        var current = new List<CraftCandidate>();

        Search(0);
        return best is null ? [] : OrderByCraftDistance(best).ToList();

        void Search(int start)
        {
            if (current.Count > 0)
            {
                var totalStun = projected.Stun + current.Sum(candidate => candidate.Metrics.Stun);
                if (totalStun <= strategy.StunCap + 0.0001)
                {
                    var distance = Math.Abs(strategy.StunTarget - totalStun);
                    var reachesTarget = totalStun + 0.0001 >= strategy.StunTarget;
                    // 조로의 봉쿠레/크로커다일처럼 전용 홀딩 축은 스턴 세트 안에서
                    // 먼저 충족한다. 그다음 스턴은 최소 기수로 채운다. 같은 1.4라도
                    // 3기 세트는 남은 추천 슬롯에서 이감·방깎·보조딜 자리를 빼앗는다.
                    var coreCoverage = Math.Min(strategy.CommunityCoreTarget,
                        current.Count(candidate => IsCommunityCore(
                            goal, candidate.Unit, strategy)));
                    var size = current.Count;
                    var usefulMetrics = current.Sum(candidate =>
                        RemainingUsefulMetricCount(candidate.Metrics, projected, strategy));
                    var overshoot = totalStun > strategy.StunTarget + 0.0001;
                    var craftScore = (
                        Completion: current.Sum(candidate =>
                            candidate.Recommendation.RecipeProgress.CompletionRatio),
                        MissingLeaves: -current.Sum(candidate =>
                            (long)candidate.Recommendation.RecipeProgress.MissingLeaves.Sum(
                                leaf => leaf.MissingCount)),
                        CommunityPriority: current.Sum(candidate =>
                            CommunityPriorityScore(goal, candidate.Unit)));
                    var sameDistance = Math.Abs(distance - bestDistance) < 0.0001;
                    var sameCore = coreCoverage == bestCoreCoverage;
                    if (best is null ||
                        reachesTarget && !bestReachesTarget ||
                        reachesTarget == bestReachesTarget &&
                        (distance < bestDistance - 0.0001 ||
                         sameDistance && coreCoverage > bestCoreCoverage ||
                         sameDistance && sameCore && size < bestSize ||
                         sameDistance && sameCore && size == bestSize &&
                         usefulMetrics > bestUsefulMetrics ||
                         sameDistance && sameCore && size == bestSize &&
                         usefulMetrics == bestUsefulMetrics && bestOvershoot && !overshoot ||
                          sameDistance && sameCore && size == bestSize &&
                          usefulMetrics == bestUsefulMetrics && bestOvershoot == overshoot &&
                          craftScore.CompareTo(bestCraftScore) > 0))
                    {
                        best = current.ToList();
                        bestReachesTarget = reachesTarget;
                        bestDistance = distance;
                        bestCoreCoverage = coreCoverage;
                        bestSize = size;
                        bestUsefulMetrics = usefulMetrics;
                        bestOvershoot = overshoot;
                        bestCraftScore = craftScore;
                    }
                }
            }

            if (current.Count >= maximumPicks) return;
            for (var index = start; index < pool.Count; index++)
            {
                var candidate = pool[index];
                var liveProjected = projected;
                foreach (var picked in current) liveProjected += picked.Metrics;
                if (!IsCompatibleSupport(goal, candidate.Unit,
                        alreadySelected.Concat(current).ToList(), inventory, liveProjected,
                        strategy)) continue;
                var totalStun = projected.Stun + current.Sum(item => item.Metrics.Stun) +
                                candidate.Metrics.Stun;
                if (totalStun > strategy.StunCap + 0.0001) continue;
                current.Add(candidate);
                Search(index + 1);
                current.RemoveAt(current.Count - 1);
            }
        }
    }

    private static bool FitsStunCap(StrategyMetrics projected, CraftCandidate candidate,
        double stunCap) =>
        candidate.Metrics.Stun <= 0 ||
        projected.Stun + candidate.Metrics.Stun <= stunCap + 0.0001;

    private List<CraftCandidate> ChooseArmorSet(UnitDefinition goal,
        GoalStrategyProfile strategy,
        StrategyMetrics projected,
        IReadOnlyCollection<CraftCandidate> remaining,
        IReadOnlyCollection<CraftCandidate> alreadySelected,
        IReadOnlyDictionary<string, int> inventory,
        int availableSlots)
    {
        if (availableSlots <= 0) return [];
        var compatible = remaining
                .Where(candidate => candidate.Metrics.ArmorReduction > 0)
                .Where(candidate => IsCompatibleSupport(goal, candidate.Unit, alreadySelected,
                    inventory, projected, strategy))
                .Where(candidate => FitsStunCap(projected, candidate, strategy.StunCap))
                .ToList();
        // 빈 패에서도 211에 닿을 수 있도록 고방깎 후보를 반드시 풀에 남기고,
        // 현재 패에서 가까운 후보를 함께 섞어 기수 최소 → 제작비 최소 비교를 한다.
        var pool = compatible
            .OrderByDescending(candidate => candidate.Metrics.ArmorReduction)
            .ThenByDescending(candidate => candidate.Recommendation.RecipeProgress.CompletionRatio)
            .Take(10)
            .Concat(OrderByCraftDistance(compatible).Take(10))
            .DistinctBy(candidate => candidate.Unit.Id, StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToList();
        var maximumPicks = Math.Min(pool.Count, availableSlots);
        List<CraftCandidate>? best = null;
        var bestReachesTarget = false;
        var bestSize = int.MaxValue;
        var bestMissingLeaves = long.MaxValue;
        var bestOvershoot = double.MaxValue;
        var bestUsefulMetrics = -1;
        var bestCommunityScore = double.MinValue;
        var bestContribution = double.MinValue;
        var current = new List<CraftCandidate>();
        var missingLeafCosts = pool
            .Select(candidate => candidate.Recommendation.RecipeProgress.MissingLeaves
                .Sum(leaf => (long)leaf.MissingCount))
            .ToArray();
        var usefulMetricCounts = pool
            .Select(candidate =>
                RemainingUsefulMetricCount(candidate.Metrics, projected, strategy))
            .ToArray();
        var communityScores = pool
            .Select(candidate => CommunityPriorityScore(goal, candidate.Unit))
            .ToArray();

        Search(0, contribution: 0, missingLeaves: 0, usefulMetrics: 0,
            communityScore: 0);
        return best is null ? [] : OrderByCraftDistance(best).ToList();

        void Search(int start, double contribution, long missingLeaves,
            int usefulMetrics, double communityScore)
        {
            if (current.Count > 0)
            {
                var reachesTarget = projected.ArmorReduction + contribution + 0.0001 >=
                                    strategy.ArmorReductionTarget;
                var size = current.Count;
                var overshoot = reachesTarget
                    ? projected.ArmorReduction + contribution - strategy.ArmorReductionTarget
                    : double.MaxValue;
                var better = best is null ||
                             reachesTarget && !bestReachesTarget ||
                             reachesTarget == bestReachesTarget &&
                             (reachesTarget
                                 ? size < bestSize ||
                                   size == bestSize && missingLeaves < bestMissingLeaves ||
                                   size == bestSize && missingLeaves == bestMissingLeaves &&
                                   overshoot < bestOvershoot - 0.0001 ||
                                   size == bestSize && missingLeaves == bestMissingLeaves &&
                                   Math.Abs(overshoot - bestOvershoot) < 0.0001 &&
                                   usefulMetrics > bestUsefulMetrics ||
                                   size == bestSize && missingLeaves == bestMissingLeaves &&
                                   Math.Abs(overshoot - bestOvershoot) < 0.0001 &&
                                   usefulMetrics == bestUsefulMetrics &&
                                   communityScore > bestCommunityScore
                                 : contribution > bestContribution + 0.0001 ||
                                   Math.Abs(contribution - bestContribution) < 0.0001 &&
                                   size < bestSize ||
                                   Math.Abs(contribution - bestContribution) < 0.0001 &&
                                   size == bestSize && missingLeaves < bestMissingLeaves);
                if (better)
                {
                    best = current.ToList();
                    bestReachesTarget = reachesTarget;
                    bestSize = size;
                    bestMissingLeaves = missingLeaves;
                    bestOvershoot = overshoot;
                    bestUsefulMetrics = usefulMetrics;
                    bestCommunityScore = communityScore;
                    bestContribution = contribution;
                }
            }

            // 목표를 채운 최소 기수를 찾은 뒤에는 그보다 큰 조합을 탐색하지 않는다.
            if (bestReachesTarget && current.Count >= bestSize) return;
            if (current.Count >= maximumPicks) return;
            for (var index = start; index < pool.Count; index++)
            {
                var candidate = pool[index];
                current.Add(candidate);
                Search(index + 1,
                    contribution + candidate.Metrics.ArmorReduction,
                    missingLeaves + missingLeafCosts[index],
                    usefulMetrics + usefulMetricCounts[index],
                    communityScore + communityScores[index]);
                current.RemoveAt(current.Count - 1);
            }
        }
    }

    private static IOrderedEnumerable<CraftCandidate> OrderByCraftDistance(
        IEnumerable<CraftCandidate> candidates) => candidates
        .OrderByDescending(candidate => candidate.Recommendation.RecipeProgress.CompletionRatio)
        .ThenBy(candidate => candidate.Recommendation.RecipeProgress.MissingLeaves.Sum(leaf => leaf.MissingCount))
        .ThenBy(candidate => candidate.Recommendation.RecipeProgress.RequiredLeafCount)
        .ThenByDescending(candidate => candidate.Metrics.Total)
        .ThenBy(candidate => candidate.Recommendation.Route.Name, StringComparer.CurrentCulture);

    private List<Recommendation> OrderCompatibleByCraftDistance(
        UnitDefinition goal,
        IReadOnlyDictionary<string, int> inventory,
        IEnumerable<CraftCandidate> candidates,
        int take)
    {
        var selected = new List<CraftCandidate>();
        var projected = AggregateStrategyMetrics(inventory);
        var compatibilityOnly = new GoalStrategyProfile(
            0, 0, SlowTarget: 0, StunTarget: 0,
            ArmorReductionTarget: 0, AirMovementTarget: 0,
            StunCap: double.MaxValue);
        foreach (var candidate in OrderByCraftDistance(candidates))
        {
            if (selected.Count >= take) break;
            if (!IsCompatibleSupport(goal, candidate.Unit, selected, inventory,
                    projected, compatibilityOnly))
                continue;
            selected.Add(candidate);
            projected += candidate.Metrics;
        }
        return selected.Select(candidate => candidate.Recommendation).ToList();
    }

    private IOrderedEnumerable<CraftCandidate> OrderTowardsTarget(
        IEnumerable<CraftCandidate> candidates,
        double current,
        double target,
        Func<CraftCandidate, double> contribution,
        StrategyMetrics projected,
        GoalStrategyProfile strategy,
        UnitDefinition goal) => candidates
        .OrderByDescending(candidate => candidate.Recommendation.RecipeProgress.CompletionRatio >= 0.9999)
        .ThenByDescending(candidate => RemainingUsefulMetricCount(candidate.Metrics, projected, strategy))
        .ThenByDescending(candidate => candidate.Recommendation.RecipeProgress.CompletionRatio)
        .ThenBy(candidate => candidate.Recommendation.RecipeProgress.MissingLeaves.Sum(leaf => leaf.MissingCount))
        .ThenBy(candidate => candidate.Recommendation.RecipeProgress.RequiredLeafCount)
        .ThenBy(candidate => current + contribution(candidate) + 0.0001 < target ? 1 : 0)
        .ThenBy(candidate => Math.Abs(target - current - contribution(candidate)))
        .ThenByDescending(candidate => CommunityPriorityScore(goal, candidate.Unit))
        .ThenBy(candidate => candidate.Recommendation.Route.Name, StringComparer.CurrentCulture);

    // 2026-08-17 유저 검증(야마토+바헌): 비비 변화는 빌드를 다 짜고 패가 남을 때
    // 목박으로 들어가는 마무리 필러라, 클리어 동시출현(1상위 실측 51%)이 우선순위를
    // 과대평가한다. 꽉 짠 빌드에서도 54%(빌드 크기 신호 무효), 야마토 특이도 lift
    // 2.4(목표 특이도 신호 무효)라 종료 스냅샷 구조만으로는 걸러지지 않는다 —
    // 이런 유닛은 실측 채용률 대신 수작업 커뮤니티 테이블 순위로 후퇴시킨다.
    private static readonly IReadOnlySet<string> LeftoverFillerRawcodes =
        new HashSet<string>(StringComparer.Ordinal) { "W50h" }; // 비비 변화

    // 풀이감·풀깎을 맞출 때 전설 다음에 넣는 희귀/특별 필러(짤이감·짤깍).
    private static readonly IReadOnlySet<string> CheapSlowRawcodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "K50h", // 페로나 희귀 이감20
            "D20h", // 키드 희귀 이감15
            "H20h", // 크로커다일 희귀 이감15
            "B20h", // 아오키지 희귀 이감10
            "F10h", // 스모커 특별 이감5
            "X90h"  // 드레이크 희귀 이감10 깍5
        };

    private static readonly IReadOnlySet<string> CheapArmorRawcodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "U10h", // 바질 희귀 깍10
            "X90h", // 드레이크 희귀 이감10 깍5
            "E10h", // 쵸파 두뇌강화 깍3
            "610h"  // 바질 특별 깍3
        };

    private static readonly IReadOnlySet<string> CheapBuffRawcodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "K20h", // 쵸파 혼포인트 희귀 공증25
            "D10h", // 쵸파 가드포인트 특별 공증7
            "N10h"  // 브룩 희귀 공속10
        };

    private static bool IsCheapSlowFiller(UnitDefinition unit) =>
        unit.Rawcodes.Any(CheapSlowRawcodes.Contains);

    private static bool IsCheapArmorFiller(UnitDefinition unit) =>
        unit.Rawcodes.Any(CheapArmorRawcodes.Contains);

    private static bool IsCheapBuffFiller(UnitDefinition unit) =>
        unit.Rawcodes.Any(CheapBuffRawcodes.Contains);

    private static bool IsCheapStatFiller(UnitDefinition unit) =>
        IsCheapSlowFiller(unit) || IsCheapArmorFiller(unit) || IsCheapBuffFiller(unit);

    private static bool IsCheapFillerFor(UnitDefinition goal, UnitDefinition unit)
    {
        if (IsCheapSlowFiller(unit)) return true;
        return GoalStrategyCalculator.IsMagicDamageTier(goal.Tier) ? IsCheapBuffFiller(unit) : IsCheapArmorFiller(unit);
    }

    // 제파 전설 · 아카이누 히든. 둘 다 보잡+광보잡 스틱이라 한 보드에 같이 안 간다.
    private static readonly IReadOnlySet<string> ExclusiveBerserkStickRawcodes =
        new HashSet<string>(StringComparer.Ordinal) { "I30h", "Z30h" };

    // 가이드 43747 특강 노트 기준 "특강(필수)" 상위 — 특성포인트가 없으면 제 성능이
    // 안 나온다. 키자루 초월은 특강이 특포 반복 소모형 스킬강화라(특성공학 항법에서
    // 특포를 계속 빨아들임) 이 목록과 특포 경합이 생긴다. 실측 검증: 키자루 다상위
    // 21판의 동반 상위에 이 목록 유닛이 0회. 시키(B40h)는 마딜 용도는 특강 불필요라 제외.
    private static readonly IReadOnlySet<string> TraitHungryTopRawcodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "E90H", // 도플라밍고 초월
            "490H", // 바질호킨스 초월
            "290H", // 사보 초월
            "DB0H", // 야마토 초월
            "B90H", // 우솝 초월
            "F90H", // 조로 초월
            "2B0H", // 스네이크맨 초월
            "H90H", // 상디 초월
            "N50H", // 타시기 초월
            "940h", // 레일리 불멸
            "C50h", // 핸콕 영원
            "R80h", // 오뎅 영원
            "O80h", // 마르코(인간폼) 제한
            "Q80h"  // 알비다 제한(조합에 특포 4개 필요)
        };


    /// <summary>
    /// 세라핌처럼 후삽입되는 후보가 있어도 take 제한을 지키되, 현재 전략의 필수
    /// 역할(스턴·이감·깎기·단일·끝딜 등)을 담당하는 후보를 단순히 꼬리에서 자르지 않는다.
    /// 같은 역할 충족도를 유지하는 후보 중 채용률이 가장 낮은 항목부터 제거한다.
    /// </summary>
    private List<Recommendation> LimitRecommendationsPreservingStrategy(
        List<Recommendation> results,
        int take,
        UnitDefinition goal,
        IReadOnlyDictionary<string, int> inventory,
        GoalStrategyProfile? strategy,
        bool showGoal,
        IReadOnlyCollection<string> protectedUnitIds)
    {
        var limit = Math.Max(1, take);
        if (results.Count <= limit) return results;
        if (strategy is null) return RecommendationResultPolicy.Limit(results, limit);

        while (results.Count > limit)
        {
            var fullMetrics = ProjectedMetrics(results, excludedIndex: -1);
            var removable = Enumerable.Range(0, results.Count)
                .Select(index =>
                {
                    var recommendation = results[index];
                    var unit = catalog.Unit(recommendation.Route.GoalUnitId);
                    var without = ProjectedMetrics(results, index);
                    return new
                    {
                        Index = index,
                        Unit = unit,
                        CoverageLoss = StrategyCoverageLoss(fullMetrics, without, strategy.Value),
                        CommunityPriority = CommunityPriorityScore(goal, unit),
                        IsArmor = GoalStrategyCalculator.StrategyMetricsFor(unit)
                            .ArmorReduction > 0,
                        Completion = recommendation.RecipeProgress.CompletionRatio,
                        MissingLeaves = recommendation.RecipeProgress.MissingLeaves
                            .Sum(leaf => leaf.MissingCount),
                        IsSeraphim = BaseTier(unit.Tier) == "세라핌"
                    };
                })
                .Where(item => !item.Unit.Id.Equals(goal.Id, StringComparison.OrdinalIgnoreCase))
                .Where(item => !protectedUnitIds.Contains(item.Unit.Id,
                    StringComparer.OrdinalIgnoreCase))
                .Where(item => !IsCommunityCore(goal, item.Unit, strategy.Value))
                .OrderBy(item => item.CoverageLoss)
                // 역할 손실이 없는 중복 방깎은 현재 패에서 먼 후보부터 정리한다.
                // 비방깎 지원의 기존 채용률 순서는 그대로 유지한다.
                .ThenBy(item => item.IsArmor ? 0 : 1)
                .ThenBy(item => item.IsArmor ? item.Completion : 0)
                .ThenByDescending(item => item.IsArmor ? item.MissingLeaves : 0)
                .ThenBy(item => item.CommunityPriority)
                // 같은 손실·채용률이면 세라핌보다 일반 후보를 먼저 정리한다.
                .ThenBy(item => item.IsSeraphim ? 1 : 0)
                .ThenByDescending(item => item.Index)
                .ToList();

            if (removable.Count == 0)
                return RecommendationResultPolicy.Limit(results, limit);
            results.RemoveAt(removable[0].Index);
        }
        return results;

        StrategyMetrics ProjectedMetrics(IReadOnlyList<Recommendation> items, int excludedIndex)
        {
            var projected = AggregateStrategyMetrics(inventory);
            if (showGoal) projected += GoalStrategyCalculator.StrategyMetricsFor(goal);
            for (var index = 0; index < items.Count; index++)
            {
                if (index == excludedIndex) continue;
                var unitId = items[index].Route.GoalUnitId;
                if (unitId.Equals(goal.Id, StringComparison.OrdinalIgnoreCase)) continue;
                projected += GoalStrategyCalculator.StrategyMetricsFor(catalog.Unit(unitId));
            }
            return projected;
        }
    }

    private static double StrategyCoverageLoss(StrategyMetrics before,
        StrategyMetrics after, GoalStrategyProfile strategy) =>
        Loss(before.Slow, after.Slow, strategy.SlowTarget) +
        Loss(before.Stun, after.Stun, strategy.StunTarget) +
        Loss(before.ArmorReduction, after.ArmorReduction, strategy.ArmorReductionTarget) +
        Loss(before.ArmorBreak, after.ArmorBreak, strategy.ArmorBreakTarget) +
        Loss(before.AirMovement, after.AirMovement, strategy.AirMovementTarget) +
        Loss(before.BossControl, after.BossControl, strategy.BossControlTarget) +
        Loss(before.BerserkBossControl, after.BerserkBossControl,
            strategy.BerserkBossControlTarget) +
        Loss(before.MagicArmorReduction, after.MagicArmorReduction,
            strategy.MagicArmorReductionTarget) +
        Loss(before.SingleDamage, after.SingleDamage, strategy.SingleDamageTarget) +
        Loss(before.FinisherDamage, after.FinisherDamage, strategy.FinisherDamageTarget);

    private static double Loss(double before, double after, double target)
    {
        if (target <= 0) return 0;
        var beforeCovered = Math.Min(Math.Max(0, before), target);
        var afterCovered = Math.Min(Math.Max(0, after), target);
        return Math.Max(0, beforeCovered - afterCovered) / target;
    }

    private ClearEvidence? BuildClearEvidence(IReadOnlyList<string> candidateRawcodes)
    {
        if (_activeClearProfile is null) return null;
        var share = candidateRawcodes
            .Select(code => _activeClearProfile.SupportShare.GetValueOrDefault(code))
            .DefaultIfEmpty()
            .Max();
        if (share <= 0) return null;
        return new ClearEvidence(_activeClearProfile.SampleCount,
            (int)Math.Round(share * 100, MidpointRounding.AwayFromZero),
            _activeClearProfile.Scope, _activeAnchorLabel);
    }

    private int CommunityPriorityScore(UnitDefinition goal, UnitDefinition candidate)
    {
        // 신+ 클리어 데이터가 충분하면 실측 채용률(보유 앵커가 있으면 조건부 재집계)을
        // 쓰고, 표본이 부족한 목표는 기존 수작업 커뮤니티 테이블로 후퇴한다. 역할 코어
        // 우선 구조는 그대로이며 이 점수는 같은 역할 버킷 안의 순서만 바꾼다.
        // 비비 변화는 야마토 목박 필러라 채용률을 그대로 쓰면 안 된다.
        // 거프 1상위에서는 이감+깎 코어(45%)라 실측 채용률을 유지한다.
        var isYamatoLeftover = candidate.Rawcodes.Any(LeftoverFillerRawcodes.Contains) &&
            (goal.Id.Equals("yamato_transcendent", StringComparison.OrdinalIgnoreCase) ||
             goal.Rawcodes.Contains("DB0H", StringComparer.Ordinal));
        var clearScore = isYamatoLeftover || _activeClearProfile is null
            ? (int?)null
            : (int)Math.Round(candidate.Rawcodes
                .Select(code => _activeClearProfile.SupportShare.GetValueOrDefault(code))
                .DefaultIfEmpty()
                .Max() * 100, MidpointRounding.AwayFromZero);
        if (clearScore is not null)
            return LiveStats.ApplyWeight(clearScore.Value,
                _liveStats.WeightFor(goal.Id, candidate.Id));

        var priorities = RecommendationCommunityPriorities.ForGoal(goal);
        if (priorities is null) return 0;
        return candidate.Rawcodes.Select(rawcode => priorities.GetValueOrDefault(rawcode))
            .DefaultIfEmpty().Max();
    }

    private bool IsCommunityCore(UnitDefinition goal, UnitDefinition candidate,
        GoalStrategyProfile strategy)
    {
        if (goal.Rawcodes.Contains("F90H", StringComparer.Ordinal))
            return candidate.Rawcodes.Any(rawcode => rawcode is "F50h" or "O30h");
        return false;
    }

    private static double BuffSupportValue(UnitDefinition unit) =>
        GoalStrategyCalculator.AbilityTotal(unit, "공격력 증가") +
        GoalStrategyCalculator.AbilityTotal(unit, "공격속도 증가");

    private static int RemainingUsefulMetricCount(StrategyMetrics metrics,
        StrategyMetrics projected,
        GoalStrategyProfile strategy)
    {
        var count = 0;
        if (projected.Slow + 0.0001 < strategy.SlowTarget && metrics.Slow > 0) count++;
        if (projected.ArmorBreak + 0.0001 < strategy.ArmorBreakTarget && metrics.ArmorBreak > 0) count++;
        if (projected.Stun + 0.0001 < strategy.StunTarget && metrics.Stun > 0) count++;
        if (projected.ArmorReduction + 0.0001 < strategy.ArmorReductionTarget &&
            metrics.ArmorReduction > 0) count++;
        if (projected.MagicArmorReduction + 0.0001 < strategy.MagicArmorReductionTarget &&
            metrics.MagicArmorReduction > 0) count++;
        if (projected.SingleDamage + 0.0001 < strategy.SingleDamageTarget &&
            metrics.SingleDamage > 0) count++;
        if (projected.FinisherDamage + 0.0001 < strategy.FinisherDamageTarget &&
            metrics.FinisherDamage > 0) count++;
        if (projected.AirMovement + 0.0001 < strategy.AirMovementTarget &&
            metrics.AirMovement > 0) count++;
        if (projected.BossControl + 0.0001 < strategy.BossControlTarget && metrics.BossControl > 0) count++;
        if (projected.BerserkBossControl + 0.0001 < strategy.BerserkBossControlTarget &&
            metrics.BerserkBossControl > 0) count++;
        return count;
    }

    private bool IsCompatibleSupport(UnitDefinition goal,
        UnitDefinition candidate,
        IReadOnlyCollection<CraftCandidate> selected,
        IReadOnlyDictionary<string, int> inventory,
        StrategyMetrics projected,
        GoalStrategyProfile strategy)
    {
        if (!GoalStrategyCalculator.IsCompatibleSupportDamageType(goal, candidate))
            return false;
        if (BaseTier(candidate.Tier) == "세라핌" &&
            (selected.Any(item => BaseTier(item.Unit.Tier) == "세라핌") ||
             inventory.Any(pair => pair.Value > 0 &&
                                   BaseTier(catalog.Unit(pair.Key).Tier) == "세라핌")))
            return false;

        // 배 하나는 유닛 하나에만 들어간다. 이미 선택된 후보들이 보유한 배를 다
        // 예약했다면 추가 배 소비 후보는 함께 추천하지 않는다(유저 보고:
        // 해적선 1척에 모비딕·에넬이 동시 추천되던 문제).
        var candidateShips = RequiredShipCodes(candidate, inventory);
        if (candidateShips.Count > 0)
        {
            foreach (var shipCode in candidateShips.Distinct())
            {
                var ownedShips = inventory.GetValueOrDefault("rawcode:" + shipCode);
                var reserved = selected.Sum(item =>
                    RequiredShipCodes(item.Unit, inventory).Count(code =>
                        code.Equals(shipCode, StringComparison.Ordinal)));
                var required = candidateShips.Count(code =>
                    code.Equals(shipCode, StringComparison.Ordinal));
                if (reserved + required > ownedShips) return false;
            }
        }

        // 마딜은 물딜 짤깍(바질 희귀·쵸파 두뇌)이 역할이 없다. 이감도 있는
        // 드레이크만 짤이감으로 남긴다.
        if (GoalStrategyCalculator.IsMagicDamageTier(goal.Tier) && IsCheapArmorFiller(candidate) &&
            !IsCheapSlowFiller(candidate) && !IsCheapBuffFiller(candidate))
            return false;

        // 제파와 아카히든은 같은 보잡+광보잡 스틱이다. 신+ 상디 2451판에서
        // 제파가 있을 때 아카히든 동반은 5.8%뿐. 제파+쿠마 세라핌 보드에
        // 아카히든을 또 올리면 그 피드백이 된다. 네코·시노부 같은 2기 광보잡은 허용.
        if (candidate.Rawcodes.Any(ExclusiveBerserkStickRawcodes.Contains) &&
            (selected.Any(item => item.Unit.Rawcodes.Any(ExclusiveBerserkStickRawcodes.Contains)) ||
             inventory.Any(pair => pair.Value > 0 &&
                                   catalog.Unit(pair.Key).Rawcodes
                                       .Any(ExclusiveBerserkStickRawcodes.Contains))))
            return false;

        // 목표 기수를 채운 뒤의 순수 광보잡 추가는 막는다. 이감·끝딜을 겸하면 남긴다.
        // 목표가 0인 상위(조로·야마토)는 선택 보잡을 이 규칙으로 막지 않는다.
        if (strategy.BerserkBossControlTarget > 0 &&
            GoalStrategyCalculator.AbilityTotal(candidate, "광폭화 잡기") > 0 &&
            projected.BerserkBossControl + 0.0001 >= strategy.BerserkBossControlTarget)
        {
            var otherMetrics = GoalStrategyCalculator.StrategyMetricsFor(candidate) with { BerserkBossControl = 0 };
            if (RemainingUsefulMetricCount(otherMetrics, projected, strategy) <= 0)
                return false;
        }

        if (goal.Rawcodes.Contains("F90H", StringComparer.Ordinal) &&
            candidate.Rawcodes.Any(rawcode => rawcode is "F50h" or "O30h"))
        {
            var zoroHolderCodes = new[] { "F50h", "O30h" };
            var alreadyHasHolder = inventory.Keys
                                       .Where(id => inventory.GetValueOrDefault(id) > 0)
                                       .Select(catalog.Unit)
                                       .Any(unit => unit.Rawcodes.Any(zoroHolderCodes.Contains)) ||
                                   selected.Any(item => item.Unit.Rawcodes.Any(zoroHolderCodes.Contains));
            if (alreadyHasHolder) return false;
        }

        if (!goal.Id.Equals("yamato_transcendent", StringComparison.OrdinalIgnoreCase)) return true;
        var mainHolders = new[] { "dragon_legend", "bartolomeo_legend", "ivankov_hidden" };
        if (!mainHolders.Contains(candidate.Id, StringComparer.OrdinalIgnoreCase)) return true;

        var hasMainHolder = mainHolders.Any(id => inventory.GetValueOrDefault(id) > 0) ||
                            selected.Any(item => mainHolders.Contains(item.Unit.Id,
                                StringComparer.OrdinalIgnoreCase));
        if (hasMainHolder) return false;

        if (!candidate.Id.Equals("ivankov_hidden", StringComparison.OrdinalIgnoreCase)) return true;
        var hasGreenBlood = inventory.GetValueOrDefault("item_greenblood") > 0;
        var hasMobyDick = inventory.GetValueOrDefault("mobydick") > 0 ||
                          selected.Any(item => item.Unit.Id.Equals("mobydick",
                              StringComparison.OrdinalIgnoreCase));
        return hasGreenBlood && hasMobyDick;
    }

    internal StrategyMetrics AggregateStrategyMetrics(
        IReadOnlyDictionary<string, int> inventory)
    {
        var result = new StrategyMetrics();
        var conditionalJinbeCount = 0;
        foreach (var (unitId, count) in inventory.Where(pair => pair.Value > 0))
        {
            var unit = catalog.Unit(unitId);
            if (!CountsAsCompletedSupport(unit)) continue;
            result += GoalStrategyCalculator.StrategyMetricsFor(unit) * count;
            if (unit.Rawcodes.Contains("G30h", StringComparer.Ordinal))
                conditionalJinbeCount += count;
        }
        // TMO 43747 징베 전설: 암브 10 이상인 적에게 방깎 25를 1회 적용한다.
        // 베르고·베이비5 등 암브 기물이 실제 패에 있을 때만 유효 방깎으로 센다.
        if (conditionalJinbeCount > 0 && result.ArmorBreak > 0)
            result = result with
            {
                ArmorReduction = result.ArmorReduction + conditionalJinbeCount * 25
            };
        return result;
    }

            /// <summary>
    /// 43747 공략 문구와 자체 스턴·이감으로 상위별 목표를 보정한다.
    /// 거프처럼 손댄 규칙(자체 원스턴이면 추가 스턴 생략, 솔딜이면 커뮤니티 코어,
    /// 짤이감·짤깍)을 다른 상위에도 같은 근거로 적용한다.
    /// </summary>
            /// <summary>신+ 오로성(판별 전역 변수)에 맞춰 역할 목표를 보정한다.</summary>
                        private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();

    public IReadOnlyList<string> RecipeLegendaryUnitIds(string goalUnitId) =>
        _recipes.RecipeLegendaryUnitIds(goalUnitId);

    public IReadOnlyList<string> RecipeRareUnitIds(string goalUnitId) =>
        _recipes.RareShipTreeIds(catalog.Unit(goalUnitId)).ToList();

    public IReadOnlyList<string> RecipeSpecialUnitIds(string unitId) =>
        _recipes.RecipeSpecialUnitIds(unitId);

    /// <summary>
    /// 초월 조합식의 전설급 직접 재료. 전설이 없으면 히든을 쓴다.
    /// 스토리 진행을 위해 후보 보드에서 역할 패키지보다 앞에 둔다.
    /// </summary>
        // 목표 조합 트리 안에 있는 희귀함 유닛들. 하위 희귀함의 재료 트리까지는
    // 내려가지 않는다(그 하위는 캐스케이드 클릭으로 확인).
                // 아이템(흑도 슈스이 방깎 6 등)과 세라핌도 보드에 있으면 완성 전력으로 합산한다.
    // 짤이감·짤깍(페로나 희귀, 바질 희귀 등)은 보드에 있으면 이감/깎 합산에 넣는다.
    private static bool CountsAsCompletedSupport(string tier) =>
        BaseTier(tier) is "전설" or "히든" or "변화된" or "왜곡됨" or "함선" or "해적선" or
            "초월" or "불멸" or "영원" or "제한됨" or "아이템" or "세라핌";

    private static bool CountsAsCompletedSupport(UnitDefinition unit) =>
        CountsAsCompletedSupport(unit.Tier) || IsCheapStatFiller(unit);

    /// <summary>
    /// 긴급소집 와일드카드(특별함 선택 3장) 사용처. 추천 빌드(목표 카드 우선)에
    /// 부족한 특별함 재료부터 제작 수고가 큰 순으로 배정하고, 남는 장수는 신+
    /// 클리어 최종 조합에 남는 유틸 특별함(채용률 10퍼센트 이상)으로 채운다.
    /// RecommendNearestCrafts 직후 같은 패스에서 호출해야 조건부 프로필을 공유한다.
    /// </summary>
    public IReadOnlyList<EmergencySummonAdvice> RecommendEmergencySummons(
        IReadOnlyList<Recommendation> recommendations,
        IEnumerable<InventoryEntry> inventory)
    {
        const int wildcards = 3;
        var picks = new List<EmergencySummonAdvice>();
        var allocated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var recommendation in recommendations)
        {
            foreach (var step in recommendation.RemainingCraftSteps
                         .Where(step => BaseTier(step.Tier) == "특별함")
                         .Where(step => step.MissingCount > 0)
                         .OrderByDescending(step => step.Ingredients
                             .Sum(ingredient => ingredient.RequiredCount)))
            {
                var used = picks.Sum(pick => pick.Count);
                if (used >= wildcards) return picks;
                var already = allocated.GetValueOrDefault(step.UnitId);
                var take = Math.Min(step.MissingCount - already, wildcards - used);
                if (take <= 0) continue;
                allocated[step.UnitId] = already + take;
                picks.Add(new EmergencySummonAdvice(step.UnitId, step.Name, take,
                    $"{recommendation.Route.Name} 재료"));
            }
        }

        var remaining = wildcards - picks.Sum(pick => pick.Count);
        if (remaining <= 0 || _activeClearProfile is null) return picks;
        var owned = inventory
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        foreach (var (unit, share) in catalog.AllUnits
                     .Where(unit => BaseTier(unit.Tier) == "특별함")
                     .Where(unit => owned.GetValueOrDefault(unit.Id) <= 0 &&
                                    !allocated.ContainsKey(unit.Id))
                     .Select(unit => (unit, Share: unit.Rawcodes
                         .Select(code => _activeClearProfile.SupportShare.GetValueOrDefault(code))
                         .DefaultIfEmpty()
                         .Max()))
                     .Where(pair => pair.Share >= 0.10)
                     .OrderByDescending(pair => pair.Share)
                     .Take(remaining))
            picks.Add(new EmergencySummonAdvice(unit.Id, unit.Name, 1,
                $"신+ 최종 조합 채용률 {Math.Round(share * 100)}퍼센트"));
        return picks;
    }

    private Recommendation EvaluateCraft(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory,
        RecipeCompletionCalculator calculator) =>
        EvaluateCraft(unit, inventory, calculator, out _);

    /// <summary>
    /// 후보 탐색에는 제작 진행도와 경로 식별자만 필요하다. 화면에 노출될지 모르는
    /// 모든 후보의 조합 트리·남은 단계·특수 경고를 미리 만들지 않는다.
    /// 최종 결과는 캐스케이드 단계에서 <see cref="EvaluateCraft"/>로 완전 재구성한다.
    /// </summary>
    private static Recommendation EvaluateCraftCandidate(UnitDefinition unit,
        RecipeProgress progress)
    {
        return new Recommendation
        {
            Route = new RouteDefinition
            {
                Id = "craft:" + unit.Id,
                GoalUnitId = unit.Id,
                Name = unit.Name
            },
            Score = progress.CompletionRatio * 100,
            RecipeProgress = progress
        };
    }

    /// <summary>remainingAfterBuild = 이 유닛의 빌드가 소비하고 남는 패(순위 캐스케이드용).</summary>
    private Recommendation EvaluateCraft(UnitDefinition unit,
        IReadOnlyDictionary<string, int> inventory,
        RecipeCompletionCalculator calculator,
        out Dictionary<string, int> remainingAfterBuild)
    {
        var progress = calculator.Calculate([unit.Id], inventory);
        var missing = progress.MissingLeaves.FirstOrDefault();
        var availability = inventory
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var recipeTree = _recipes.BuildRecipeTree(unit.Id, 1, availability,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        // BuildRecipeTree가 소비하고 남긴 잔여 패 — 아래 순위 완료율 계산의 입력이 된다.
        remainingAfterBuild = availability;
        var remainingSteps = _recipes.BuildRemainingCraftSteps(recipeTree, inventory, calculator);
        var missingSpecials = CollectMissingSpecials(unit, inventory);
        var nextAction = missingSpecials.Count > 0
            ? "먼저 필요: " + string.Join(" · ", missingSpecials) +
              " — 없으면 마지막에 조합이 막힙니다"
            : missing is not null
                ? "부족한 패: " + string.Join(" · ", progress.MissingLeaves
                    .Select(leaf => $"{leaf.Name} ×{leaf.MissingCount}"))
                : remainingSteps.Count > 0
                    ? $"최하위 재료 확보 — 아래 {remainingSteps.Count}단계를 차례로 조합하면 완성"
                    : "지금 바로 조합할 수 있습니다.";
        return new Recommendation
        {
            Route = new RouteDefinition
            {
                Id = "craft:" + unit.Id,
                GoalUnitId = unit.Id,
                Name = unit.Name
            },
            Score = progress.CompletionRatio * 100,
            MissingUnits = progress.MissingLeaves.Select(leaf => leaf.Name).ToList(),
            Warnings = missingSpecials,
            MissingSpecials = missingSpecials,
            NextAction = nextAction,
            RecipeProgress = progress,
            CompositionUnits =
            [
                new CompositionUnitDetail
                {
                    UnitId = unit.Id,
                    Name = unit.Name,
                    Tier = unit.Tier,
                    Image = unit.Image,
                    RequiredCount = 1,
                    SuggestedCount = 1,
                    OwnedCount = Math.Min(1, Math.Max(0, inventory.GetValueOrDefault(unit.Id))),
                    IsGoal = true,
                    IsRequired = true,
                    Abilities = unit.OfficialAbilities,
                    Description = unit.Description
                }
            ],
            RecipeTree = recipeTree,
            RemainingCraftSteps = remainingSteps,
            CombineCommands = unit.CombineCommands
        };
    }

    private static bool IsViviEternal(UnitDefinition goal) =>
        goal.Rawcodes.Contains("750h", StringComparer.Ordinal);

    private string? SelectViviExpertPartner(
        UnitDefinition goal,
        IReadOnlyDictionary<string, int> inventory,
        RecipeCompletionCalculator calculator,
        bool allowsMultipleTopUnits)
    {
        if (!IsViviEternal(goal) || !allowsMultipleTopUnits) return null;
        const string sanji = "rawcode:H90H";
        const string kid = "rawcode:4B0H";
        if (inventory.GetValueOrDefault(sanji) > 0) return sanji;
        if (inventory.GetValueOrDefault(kid) > 0) return kid;

        var sanjiProgress = calculator.Calculate([sanji], inventory);
        var kidProgress = calculator.Calculate([kid], inventory);
        var sanjiMissing = sanjiProgress.MissingLeaves.Sum(leaf => leaf.MissingCount);
        var kidMissing = kidProgress.MissingLeaves.Sum(leaf => leaf.MissingCount);
        if (kidMissing < sanjiMissing) return kid;
        if (sanjiMissing < kidMissing) return sanji;
        return kidProgress.CompletionRatio > sanjiProgress.CompletionRatio
            ? kid
            : sanji;
    }

    private static bool IsViviExpertCompatible(
        UnitDefinition goal,
        UnitDefinition candidate,
        string? partnerId,
        bool goalOwned)
    {
        if (!IsViviEternal(goal)) return true;
        if (candidate.Id is "rawcode:P30h" or "rawcode:R80h") return false;
        if (goalOwned && candidate.Id == "rawcode:Z30h") return false;
        if (IsTopTier(candidate.Tier) && candidate.Id != partnerId) return false;
        if (partnerId == "rawcode:4B0H" &&
            GoalStrategyCalculator.StrategyMetricsFor(candidate).Stun > 0)
            return false;
        return true;
    }

    private List<Recommendation> AddViviExpertSupports(
        IReadOnlyList<Recommendation> selected,
        IReadOnlyList<CraftCandidate> candidates,
        string? partnerId,
        IReadOnlyDictionary<string, int> inventory,
        bool goalOwned,
        Func<UnitDefinition, Recommendation> evaluate)
    {
        var byId = candidates.ToDictionary(
            candidate => candidate.Unit.Id,
            candidate => candidate.Recommendation,
            StringComparer.OrdinalIgnoreCase);
        var priorityIds = new List<string>();
        if (!goalOwned) priorityIds.Add("rawcode:Z30h");
        priorityIds.AddRange(
        [
            "rawcode:780h",
            partnerId ?? "",
            "rawcode:640h",
            "mobydick"
        ]);
        foreach (var id in priorityIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (byId.ContainsKey(id) || inventory.GetValueOrDefault(id) > 0) continue;
            var unit = catalog.Unit(id);
            if (id is "rawcode:780h" or "mobydick" &&
                !MeetsOwnedPrerequisites(unit, inventory))
                continue;
            byId[id] = evaluate(unit);
        }
        return priorityIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id!])
            .Concat(selected)
            .DistinctBy(item => item.Route.GoalUnitId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ViviExpertPriority(
        UnitDefinition goal,
        string unitId,
        string? partnerId,
        bool goalOwned)
    {
        if (!IsViviEternal(goal)) return 0;
        if (!goalOwned && unitId == "rawcode:Z30h") return 600;
        if (unitId == "rawcode:780h") return 500;
        if (unitId == partnerId) return 450;
        if (unitId == "rawcode:640h") return 400;
        if (unitId == "mobydick") return 350;
        return 0;
    }

    private static bool IsRecommendedCraftTier(string tier, bool allowsMultipleTopUnits)
    {
        var baseTier = tier.Split('[', 2)[0].Trim();
        // 세라핌은 그린블러드로 제작하는 지원 유닛 — 어떤 세라핌을 만드는지가
        // 목표별로 갈리므로(상디=S-베어, 징베=S-호크 실측) 조합 후보에 포함한다.
        if (baseTier is "전설" or "히든" or "변화된" or "왜곡됨" or "함선" or "해적선" or "세라핌")
            return true;
        return allowsMultipleTopUnits &&
               baseTier is "신비함" or "초월" or "불멸" or "영원" or "제한됨";
    }

    private static bool IsTopTier(string tier)
    {
        var baseTier = tier.Split('[', 2)[0].Trim();
        return baseTier is "신비함" or "초월" or "불멸" or "영원" or "제한됨";
    }

    private static bool IsResourcePseudo(UnitDefinition unit) =>
        unit.Tier.Equals("자원", StringComparison.OrdinalIgnoreCase) ||
        unit.Rawcodes.Any(rawcode => rawcode is "GOLD" or "LUMBER" or "POINT" or "RANDOM");

    private Recommendation Evaluate(
        RouteDefinition route,
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyDictionary<UnitRole, double> ownedRoles,
        RecipeCompletionCalculator recipeCalculator)
    {
        var required = route.RequiredUnits.Select(catalog.Unit).ToList();
        var ready = new List<string>();
        var missing = new List<string>();
        var supportProgress = recipeCalculator.Calculate(route.RequiredUnits, inventory);
        var compositionIds = new[] { route.GoalUnitId }.Concat(route.RequiredUnits).ToList();
        var recipeProgress = recipeCalculator.Calculate(compositionIds, inventory);

        foreach (var unit in required)
        {
            if (inventory.GetValueOrDefault(unit.Id) > 0)
            {
                ready.Add(unit.Name);
                continue;
            }

            var unitProgress = recipeCalculator.Calculate([unit.Id], inventory);
            if (unitProgress.RequiredLeafCount <= 1 && unit.Recipe.Count == 0)
            {
                missing.Add(unit.Name);
                continue;
            }
            missing.Add($"{unit.Name} ({unitProgress.OwnedLeafCount}/{unitProgress.RequiredLeafCount})");
        }

        var projectedRoles = new Dictionary<UnitRole, double>(ownedRoles);
        foreach (var unit in required.Where(x => inventory.GetValueOrDefault(x.Id) == 0))
            foreach (var role in unit.Roles)
                projectedRoles[role.Role] = projectedRoles.GetValueOrDefault(role.Role) + role.Value;

        var roleStatuses = route.DesiredRoles
            .Select(x => new RoleStatus(x.Key, projectedRoles.GetValueOrDefault(x.Key), x.Value))
            .ToList();
        var roleFit = roleStatuses.Count == 0 ? 1 : roleStatuses.Average(x => x.Ratio);
        var completion = supportProgress.CompletionRatio;

        var warnings = new List<string>();
        var penalty = 0d;
        foreach (var pair in route.ForbiddenTogether)
        {
            var ids = pair.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length > 1 && ids.All(x => inventory.GetValueOrDefault(x) > 0))
            {
                penalty += 14;
                warnings.Add("홀딩 과투자 가능성");
            }
        }

        var holdingTarget = route.DesiredRoles.GetValueOrDefault(UnitRole.Holding);
        var holdingValue = projectedRoles.GetValueOrDefault(UnitRole.Holding);
        if (holdingTarget > 0 && holdingValue > holdingTarget * 1.65)
        {
            penalty += Math.Min(18, (holdingValue / holdingTarget - 1.65) * 12);
            warnings.Add("홀딩이 충분합니다. 남는 패는 방깎/딜에 투자하세요.");
        }

        var requiredTagsAvailable = route.RequiredTags.Count == 0 || route.RequiredTags.All(tag =>
            inventory.Keys.Select(catalog.Unit).Any(x => x.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
        if (!requiredTagsAvailable)
        {
            penalty += 20;
            warnings.Add($"조건 필요: {string.Join(", ", route.RequiredTags.Select(KoreanLabels.Tag))}");
        }

        var score = Math.Clamp(route.BaseScore + completion * CompletionWeight + roleFit * RoleWeight - penalty, 0, 100);
        var weakest = roleStatuses.OrderBy(x => x.Ratio).FirstOrDefault();
        var next = missing.Count > 0
            ? $"우선 확보: {missing[0]}"
            : weakest is { Ratio: < 1 }
                ? $"다음 보강: {RoleLabels.Name(weakest.Role)}"
                : "구성 완성 — 딜/유틸 보강";

        return new Recommendation
        {
            Route = route,
            Score = score,
            ReadyUnits = ready,
            MissingUnits = missing,
            Warnings = warnings.Distinct().ToList(),
            Roles = roleStatuses,
            NextAction = next,
            RecipeProgress = recipeProgress,
            CompositionUnits = CompositionUnits(route, inventory),
            CombineCommands = catalog.Unit(route.GoalUnitId).CombineCommands
        };
    }

    private List<CompositionUnitDetail> CompositionUnits(RouteDefinition route,
        IReadOnlyDictionary<string, int> inventory)
    {
        var specifications = new List<(string UnitId, bool IsGoal, bool IsRequired)>
        {
            (route.GoalUnitId, true, true)
        };
        specifications.AddRange(route.RequiredUnits.Select(id => (id, false, true)));
        specifications.AddRange(route.OptionalUnits.Select(id => (id, false, false)));

        return specifications
            .GroupBy(item => item.UnitId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var unit = catalog.Unit(group.Key);
                var isGoal = group.Any(x => x.IsGoal);
                var isRequired = group.Any(x => x.IsRequired);
                var suggestedCount = group.Count();
                return new CompositionUnitDetail
                {
                    UnitId = unit.Id,
                    Name = unit.Name,
                    Tier = unit.Tier,
                    Image = unit.Image,
                    RequiredCount = isRequired ? group.Count(x => x.IsRequired) : 0,
                    SuggestedCount = suggestedCount,
                    OwnedCount = Math.Min(suggestedCount, Math.Max(0, inventory.GetValueOrDefault(unit.Id))),
                    IsGoal = isGoal,
                    IsRequired = isRequired,
                    Abilities = unit.OfficialAbilities,
                    Description = unit.Description
                };
            })
            .OrderByDescending(unit => unit.IsGoal)
            .ThenByDescending(unit => unit.IsRequired)
            .ThenBy(unit => unit.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private Dictionary<UnitRole, double> AggregateRoles(IReadOnlyDictionary<string, int> inventory)
    {
        var values = new Dictionary<UnitRole, double>();
        foreach (var (id, count) in inventory)
        {
            foreach (var role in catalog.Unit(id).Roles)
                values[role.Role] = values.GetValueOrDefault(role.Role) + role.Value * count;
        }
        return values;
    }

    private sealed record CraftCandidate(UnitDefinition Unit, Recommendation Recommendation,
        StrategyMetrics Metrics);
}

public static class RoleLabels
{
    public static string Name(UnitRole role) => role switch
    {
        UnitRole.Holding => "홀딩",
        UnitRole.Slow => "이감",
        UnitRole.ArmorReduction => "방깎",
        UnitRole.AttackBoost => "공증",
        UnitRole.AreaControl => "광잡",
        UnitRole.BossControl => "보잡",
        UnitRole.Damage => "딜",
        _ => "지원"
    };
}
