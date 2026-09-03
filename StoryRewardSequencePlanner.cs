using System.Collections.Immutable;
using System.Globalization;

namespace OrandOverlay;

public enum RecommendationSequenceStage
{
    FirstRare,
    StoryReward,
    FirstLegend,
    RareReward,
    TopAndNavigation
}

public enum StorySequenceAction
{
    FindFirstRare,
    WaitForStoryReward,
    SpendStoryWisps,
    CraftLegendNow,
    BuildNearestLegend,
    PushStoryForRareReward,
    SpendRareWisps,
    WaitForRound20,
    RecommendTopAndNavigation
}

public enum RecommendationSurface
{
    FastRare,
    StoryLegend,
    TopAndNavigation
}

public sealed record StoryRewardSequenceInput
{
    public required PlannerPhase Phase { get; init; }
    public required int Round { get; init; }
    public required int? ActiveStoryStage { get; init; }
    public required int CompletedStoryStage { get; init; }
    public required IReadOnlyDictionary<string, int> RewardWisps { get; init; }
    public required IReadOnlyList<InventoryEntry> Inventory { get; init; }
    public required IReadOnlyDictionary<string, UnitDefinition> Units { get; init; }
    public required ImmutableArray<StoryStage> StoryStages { get; init; }
    public string? PendingLegendId { get; init; }
}

public sealed record StoryRewardSequenceDecision(
    RecommendationSequenceStage Stage,
    StorySequenceAction Action,
    string CurrentStoryLabel,
    string ClearRewardSummary,
    string OutcomeValueSummary,
    string ActionSummary,
    string StepSummary,
    string? RecommendedLegendId,
    string? RecommendedLegendName,
    int ExpectedUsefulUnitBp,
    bool TopNavigationUnlocked)
{
    public ImmutableArray<string> RankedLegendIds { get; init; } = [];
}

public static class RecommendationSequencePolicy
{
    public static RecommendationSurface Surface(bool automaticGoal,
        PlannerPhase phase, ManualLatches latches)
    {
        if (!automaticGoal || latches.GoalOverride)
            return RecommendationSurface.TopAndNavigation;
        return phase switch
        {
            PlannerPhase.AwaitFirstRare => RecommendationSurface.FastRare,
            PlannerPhase.Committed => RecommendationSurface.TopAndNavigation,
            _ => RecommendationSurface.StoryLegend
        };
    }

    public static RecommendationSurface Surface(StoryRewardSequenceDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.TopNavigationUnlocked)
            return RecommendationSurface.TopAndNavigation;
        return decision.Stage == RecommendationSequenceStage.FirstRare
            ? RecommendationSurface.FastRare
            : RecommendationSurface.StoryLegend;
    }
}

internal static class FirstLegendRecommendationPolicy
{
    private static readonly HashSet<string> BlockedRawcodes =
        new(["X50h", "060h"], StringComparer.OrdinalIgnoreCase);

    public static ImmutableArray<UnitDefinition> Candidates(
        IEnumerable<UnitDefinition> units,
        IReadOnlyDictionary<string, UnitDefinition> unitsById,
        IReadOnlyDictionary<string, int> inventory)
    {
        var eligible = units.Where(unit =>
                BaseTier(unit.Tier) == "전설" && IsEligible(unit, unitsById))
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        var ownedRares = inventory.Where(pair => pair.Value > 0 &&
                unitsById.TryGetValue(pair.Key, out var unit) &&
                BaseTier(unit.Tier) == "희귀함")
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ownedRares.Count == 0) return eligible;

        var anchored = eligible.Where(legend => legend.Recipe.Any(pair =>
                pair.Value > 0 && ownedRares.Contains(pair.Key)))
            .ToImmutableArray();
        return anchored.IsEmpty ? eligible : anchored;
    }

    public static bool IsEligible(UnitDefinition legend,
        IReadOnlyDictionary<string, UnitDefinition> units)
    {
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return !legend.Recipe.Where(pair => pair.Value > 0).Any(pair =>
            IsBlockedId(pair.Key) ||
            units.TryGetValue(pair.Key, out var material) &&
            ContainsBlockedMaterial(material, units, visiting));
    }

    private static bool ContainsBlockedMaterial(UnitDefinition unit,
        IReadOnlyDictionary<string, UnitDefinition> units, HashSet<string> visiting)
    {
        if (IsBlockedMaterial(unit)) return true;
        if (!visiting.Add(unit.Id)) return false;
        var blocked = unit.Recipe.Where(pair => pair.Value > 0).Any(pair =>
            IsBlockedId(pair.Key) ||
            units.TryGetValue(pair.Key, out var child) &&
            ContainsBlockedMaterial(child, units, visiting));
        visiting.Remove(unit.Id);
        return blocked;
    }

    private static bool IsBlockedMaterial(UnitDefinition unit) =>
        unit.Rawcodes.Any(BlockedRawcodes.Contains) || IsBlockedId(unit.Id) ||
        BaseTier(unit.Tier) is "해적선" or "함선" or "세라핌" or "변화된";

    private static bool IsBlockedId(string id) =>
        id.Equals(RecipeWildcards.AnySeraphim, StringComparison.OrdinalIgnoreCase) ||
        id.Equals("item_greenblood", StringComparison.OrdinalIgnoreCase) ||
        BlockedRawcodes.Any(rawcode =>
            id.Equals(rawcode, StringComparison.OrdinalIgnoreCase) ||
            id.Equals("rawcode:" + rawcode, StringComparison.OrdinalIgnoreCase));

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}

public static class StoryRewardSequencePlanner
{
    private const string SpecialWispId = "e016";
    private const string UncommonWispId = "e017";
    private const string RareWispId = "e019";
    private const int MinimumWaitBenefitBp = 2000;

    public static StoryRewardSequenceDecision Evaluate(StoryRewardSequenceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CompletedStoryStage is < 0 or > 14)
            throw new ArgumentOutOfRangeException(nameof(input));

        var inventory = input.Inventory.Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var currentStage = CurrentStage(input);
        var storyLabel = StageLabel(currentStage, input.ActiveStoryStage);
        var rewardSummary = RewardSummary(currentStage);
        var firstRare = inventory.Keys.Any(id => input.Units.TryGetValue(id, out var unit) &&
            BaseTier(unit.Tier) == "희귀함");
        if (input.Phase == PlannerPhase.AwaitFirstRare && !firstRare)
            return Decision(RecommendationSequenceStage.FirstRare,
                StorySequenceAction.FindFirstRare, storyLabel, rewardSummary,
                "첫 희귀함을 가장 빠른 완성 순으로 계산합니다.",
                "가장 가까운 희귀함부터 완성하세요.");

        if (input.Phase == PlannerPhase.Committed)
            return Decision(RecommendationSequenceStage.TopAndNavigation,
                StorySequenceAction.RecommendTopAndNavigation, storyLabel, rewardSummary,
                "스토리 위습 사용 결과가 현재 패에 반영됐습니다.",
                "갱신된 패로 가장 가까운 상위와 항법을 함께 확인하세요.",
                topNavigationUnlocked: true);

        if (input.Phase is PlannerPhase.AwaitMarineford or PlannerPhase.SpendRares or
            PlannerPhase.CommitRound20)
            return AfterFirstLegend(input, currentStage, storyLabel, rewardSummary);

        return BeforeFirstLegend(input, inventory, currentStage, storyLabel,
            rewardSummary);
    }

    private static StoryRewardSequenceDecision BeforeFirstLegend(
        StoryRewardSequenceInput input,
        IReadOnlyDictionary<string, int> inventory,
        StoryStage? currentStage,
        string storyLabel,
        string rewardSummary)
    {
        var drawCounts = LegendRewardDraws(input);
        var candidates = LegendCandidates(input, inventory, drawCounts);
        var rankedLegendIds = candidates.Select(candidate => candidate.Id).ToImmutableArray();
        var selected = input.PendingLegendId is { Length: > 0 } pending
            ? candidates.FirstOrDefault(candidate => candidate.Id.Equals(
                  pending, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault()
            : candidates.FirstOrDefault();
        if (selected is null)
            return Decision(RecommendationSequenceStage.FirstLegend,
                StorySequenceAction.BuildNearestLegend, storyLabel, rewardSummary,
                "전설 조합식을 확인할 수 없습니다.",
                "전설 후보 데이터를 확인할 때까지 상위 추천을 보류합니다.") with
            {
                RankedLegendIds = rankedLegendIds
            };

        var observed = Count(input.RewardWisps, SpecialWispId) +
                       Count(input.RewardWisps, UncommonWispId);
        var upcoming = CurrentStageLegendRewardCount(input);
        var value = selected.MissingLeaves == 0
            ? "즉시 완성 가능 · 보상보다 스토리 속도 우선"
            : $"{selected.Name} 결손 기대 {Percent(selected.ExpectedUsefulBp)} 감소 · " +
              selected.UsefulOutcomeSummary;
        if (observed > 0)
            return Decision(RecommendationSequenceStage.StoryReward,
                StorySequenceAction.SpendStoryWisps, storyLabel, rewardSummary, value,
                "보유한 특별·안흔 위습을 사용하고 나온 유닛을 다시 인식하세요.",
                selected.Id, selected.Name, selected.ExpectedUsefulBp) with
            {
                RankedLegendIds = rankedLegendIds
            };
        if (selected.MissingLeaves == 0)
            return Decision(RecommendationSequenceStage.FirstLegend,
                StorySequenceAction.CraftLegendNow, storyLabel, rewardSummary, value,
                $"{selected.Name}을 지금 조합해 스토리 속도를 확보하세요.",
                selected.Id, selected.Name, selected.ExpectedUsefulBp) with
            {
                RankedLegendIds = rankedLegendIds
            };
        if (upcoming > 0 && selected.MissingLeaves > 1 &&
            selected.ExpectedUsefulBp >= MinimumWaitBenefitBp &&
            (currentStage?.Ordinal ?? input.ActiveStoryStage) is <= 6)
            return Decision(RecommendationSequenceStage.StoryReward,
                StorySequenceAction.WaitForStoryReward, storyLabel, rewardSummary, value,
                "현재 스토리 클리어 보상을 확인한 뒤 전설 조합을 다시 계산하세요.",
                selected.Id, selected.Name, selected.ExpectedUsefulBp) with
            {
                RankedLegendIds = rankedLegendIds
            };
        return Decision(RecommendationSequenceStage.FirstLegend,
            StorySequenceAction.BuildNearestLegend, storyLabel, rewardSummary, value,
            $"추가 특별·안흔 보상 이득이 작아 {selected.Name} 조합을 진행하세요.",
            selected.Id, selected.Name, selected.ExpectedUsefulBp) with
        {
            RankedLegendIds = rankedLegendIds
        };
    }

    private static StoryRewardSequenceDecision AfterFirstLegend(
        StoryRewardSequenceInput input,
        StoryStage? currentStage,
        string storyLabel,
        string rewardSummary)
    {
        var rareWisps = Count(input.RewardWisps, RareWispId);
        if (rareWisps > 0)
            return Decision(RecommendationSequenceStage.RareReward,
                StorySequenceAction.SpendRareWisps, storyLabel, rewardSummary,
                $"희귀위습 {rareWisps}개 결과가 상위 결손을 바꿀 수 있습니다.",
                "희귀위습을 사용하고 실제 결과를 반영한 뒤 상위를 계산하세요.");

        var activeOrdinal = currentStage?.Ordinal ?? input.ActiveStoryStage ??
                            input.CompletedStoryStage + 1;
        if (activeOrdinal <= 8 && FutureRareRewardCount(input) > 0)
            return Decision(RecommendationSequenceStage.RareReward,
                StorySequenceAction.PushStoryForRareReward, storyLabel, rewardSummary,
                "남은 희귀위습은 실제 결과를 본 뒤 상위 가치로 계산합니다.",
                "스토리를 밀어 희귀위습 보상을 받은 뒤 상위 확정을 진행하세요.");

        return Decision(RecommendationSequenceStage.TopAndNavigation,
            StorySequenceAction.WaitForRound20, storyLabel, rewardSummary,
            "스토리 희귀 보상 사용 결과가 현재 패에 반영됐습니다.",
            input.Round < 20
                ? "20라운드까지 현재 패를 유지하며 상위 후보를 계속 갱신합니다."
                : "상위 경로와 항법의 안전한 승자를 계산하는 중입니다.",
            topNavigationUnlocked: true);
    }

    private static ImmutableArray<LegendProjection> LegendCandidates(
        StoryRewardSequenceInput input,
        IReadOnlyDictionary<string, int> inventory,
        IReadOnlyDictionary<string, int> draws)
    {
        var calculator = new RecipeCompletionCalculator(id => input.Units[id]);
        var candidates = FirstLegendRecommendationPolicy.Candidates(
            input.Units.Values, input.Units, inventory);
        var baseOwnedLeaves = new long[candidates.Length];
        var missingLeaves = new int[candidates.Length];
        var expectedUsefulLeaves = new decimal[candidates.Length];
        var usefulOutcomes = Enumerable.Range(0, candidates.Length)
            .Select(_ => new Dictionary<string, List<UnitDefinition>>(
                StringComparer.Ordinal))
            .ToArray();

        for (var index = 0; index < candidates.Length; index++)
        {
            var progress = calculator.CalculateAllocation(
                [candidates[index].Id], inventory).Progress;
            baseOwnedLeaves[index] = progress.OwnedLeafCount;
            missingLeaves[index] = checked((int)Math.Min(int.MaxValue,
                Math.Max(0, progress.RequiredLeafCount - progress.OwnedLeafCount)));
        }

        foreach (var (tier, count) in draws.Where(pair => pair.Value > 0))
        {
            var pool = input.Units.Values.Where(unit => BaseTier(unit.Tier) == tier)
                .OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
            if (pool.Length == 0) continue;
            var probability = 1m / pool.Length;
            foreach (var unit in pool)
            {
                for (var copies = 1; copies <= count; copies++)
                {
                    var projected = inventory.ToDictionary(pair => pair.Key,
                        pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                    projected[unit.Id] = inventory.GetValueOrDefault(unit.Id) + copies;

                    var selectedIndex = -1;
                    long selectedImprovement = 0;
                    for (var index = 0; index < candidates.Length; index++)
                    {
                        var owned = calculator.CalculateAllocation(
                            [candidates[index].Id], projected).Progress.OwnedLeafCount;
                        var improvement = Math.Min(missingLeaves[index],
                            Math.Max(0, owned - baseOwnedLeaves[index]));
                        if (improvement <= selectedImprovement) continue;
                        selectedIndex = index;
                        selectedImprovement = improvement;
                    }

                    if (selectedIndex < 0) continue;
                    expectedUsefulLeaves[selectedIndex] +=
                        BinomialProbability(count, copies, probability) *
                        selectedImprovement;
                    if (copies == 1)
                    {
                        if (!usefulOutcomes[selectedIndex].TryGetValue(tier, out var useful))
                        {
                            useful = [];
                            usefulOutcomes[selectedIndex][tier] = useful;
                        }
                        useful.Add(unit);
                    }
                }
            }
        }

        return candidates.Select((unit, index) =>
            {
                var expected = Math.Min(missingLeaves[index],
                    expectedUsefulLeaves[index]);
                var expectedBp = missingLeaves[index] == 0 ? 10_000 : Math.Clamp(
                    checked((int)Math.Round(expected * 10_000m / missingLeaves[index],
                        MidpointRounding.AwayFromZero)), 0, 10_000);
                return new LegendProjection(unit.Id, unit.Name, missingLeaves[index],
                    expected, expectedBp, UsefulOutcomeSummary(usefulOutcomes[index],
                        input.Units.Values, draws));
            })
            .OrderBy(candidate => candidate.MissingLeaves == 0 ? 0 : 1)
            .ThenBy(candidate => candidate.MissingLeaves - candidate.ExpectedUsefulLeaves)
            .ThenBy(candidate => candidate.MissingLeaves)
            .ToImmutableArray();
    }

    private static string UsefulOutcomeSummary(
        IReadOnlyDictionary<string, List<UnitDefinition>> usefulOutcomes,
        IEnumerable<UnitDefinition> units,
        IReadOnlyDictionary<string, int> draws)
    {
        var summaries = new List<string>();
        foreach (var (tier, count) in draws.Where(pair => pair.Value > 0))
        {
            var pool = units.Where(unit => BaseTier(unit.Tier) == tier)
                .OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
            if (pool.Length == 0) continue;
            if (!usefulOutcomes.TryGetValue(tier, out var useful) || useful.Count == 0)
                continue;
            var names = string.Join(", ", useful.Take(3).Select(unit => unit.Name));
            if (useful.Count > 3) names += $" 외 {useful.Count - 3}";
            summaries.Add($"{TierLabel(tier)} 유효 {names} ({useful.Count}/{pool.Length})");
        }
        return summaries.Count == 0
            ? "현재 보상 유효 후보 없음"
            : string.Join(" · ", summaries);
    }

    private static decimal BinomialProbability(int trials, int successes, decimal probability)
    {
        decimal combinations = 1;
        for (var index = 1; index <= successes; index++)
            combinations = combinations * (trials - successes + index) / index;
        return combinations * Power(probability, successes) *
               Power(1m - probability, trials - successes);
    }

    private static decimal Power(decimal value, int exponent)
    {
        decimal result = 1;
        for (var index = 0; index < exponent; index++) result *= value;
        return result;
    }

    private static IReadOnlyDictionary<string, int> LegendRewardDraws(
        StoryRewardSequenceInput input) => new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["특별함"] = Count(input.RewardWisps, SpecialWispId) +
                  CurrentStageWispCount(input, SpecialWispId),
        ["안흔함"] = Count(input.RewardWisps, UncommonWispId) +
                  CurrentStageWispCount(input, UncommonWispId)
    };

    private static int CurrentStageLegendRewardCount(StoryRewardSequenceInput input) =>
        CurrentStageWispCount(input, SpecialWispId) +
        CurrentStageWispCount(input, UncommonWispId);

    private static int CurrentStageWispCount(StoryRewardSequenceInput input, string id)
    {
        var stage = CurrentStage(input);
        return stage?.RewardComponents.EveryPlayerBase
                   .Where(component => component.Kind == "Unit" && component.Id == id)
                   .Sum(component => checked((int)component.Amount)) ?? 0;
    }

    private static int FutureRareRewardCount(StoryRewardSequenceInput input) =>
        FutureWispCount(input, RareWispId, 8);

    private static int FutureWispCount(StoryRewardSequenceInput input,
        string id, int lastStage)
    {
        var start = input.ActiveStoryStage ?? input.CompletedStoryStage + 1;
        return input.StoryStages.Where(stage => stage.Ordinal >= start &&
                                               stage.Ordinal <= lastStage)
            .SelectMany(stage => stage.RewardComponents.EveryPlayerBase)
            .Where(component => component.Kind == "Unit" && component.Id == id)
            .Sum(component => checked((int)component.Amount));
    }

    private static StoryStage? CurrentStage(StoryRewardSequenceInput input)
    {
        var ordinal = input.ActiveStoryStage ?? input.CompletedStoryStage + 1;
        return input.StoryStages.FirstOrDefault(stage => stage.Ordinal == ordinal);
    }

    private static string StageLabel(StoryStage? stage, int? activeStage)
    {
        if (stage?.MilestoneId == "Marineford") return "어인섬 · 스토리 9";
        var ordinal = stage?.Ordinal ?? activeStage;
        return ordinal is null or <= 0 ? "스토리 단계 확인 중" : $"스토리 {ordinal}";
    }

    private static string RewardSummary(StoryStage? stage)
    {
        if (stage is null) return "클리어 보상 확인 중";
        var rewards = stage.RewardComponents.EveryPlayerBase
            .Select(RewardLabel).Where(value => value.Length > 0).ToArray();
        return rewards.Length == 0 ? "기본 클리어 보상 없음" : string.Join(" · ", rewards);
    }

    private static string RewardLabel(StoryRewardComponent component)
    {
        var amount = component.Amount.ToString("N0", CultureInfo.InvariantCulture);
        return component.Kind switch
        {
            "Gold" => $"골드 {amount}",
            "Lumber" => $"목재 {amount}",
            "TraitPoint" => $"특성 포인트 {amount}",
            "Unit" => component.Id switch
            {
                "e016" => $"특별위습 {amount}",
                "e017" => $"안흔위습 {amount}",
                "e018" => $"흔함선택위습 {amount}",
                "e019" => $"희귀위습 {amount}",
                "e01A" => $"초월위습 {amount}",
                "e0IX" => $"랜덤위습 {amount}",
                _ => $"고정 유닛 보상 {amount}"
            },
            _ => ""
        };
    }

    private static StoryRewardSequenceDecision Decision(
        RecommendationSequenceStage stage,
        StorySequenceAction action,
        string storyLabel,
        string rewardSummary,
        string valueSummary,
        string actionSummary,
        string? legendId = null,
        string? legendName = null,
        int expectedUsefulBp = 0,
        bool topNavigationUnlocked = false) => new(
            stage, action, storyLabel, rewardSummary, valueSummary, actionSummary,
            StepSummary(stage, topNavigationUnlocked), legendId, legendName,
            expectedUsefulBp, topNavigationUnlocked);

    private static string StepSummary(RecommendationSequenceStage current,
        bool unlocked)
    {
        var labels = new[]
        {
            "첫 희귀함", "스토리 보상", "첫 전설", "희귀 보상", "상위+항법"
        };
        var currentIndex = (int)current;
        return string.Join('\n', labels.Select((label, index) =>
        {
            var status = index < currentIndex || unlocked && index == currentIndex
                ? "완료"
                : index == currentIndex ? "현재" : "대기";
            return $"{index + 1} {label} [{status}]";
        }));
    }

    private static int Count(IReadOnlyDictionary<string, int> values, string id) =>
        Math.Max(0, values.GetValueOrDefault(id));

    private static string Percent(int basisPoints) =>
        (basisPoints / 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();

    private static string TierLabel(string tier) => tier switch
    {
        "특별함" => "특별",
        "안흔함" => "안흔",
        _ => tier
    };

    private sealed record LegendProjection(
        string Id,
        string Name,
        int MissingLeaves,
        decimal ExpectedUsefulLeaves,
        int ExpectedUsefulBp,
        string UsefulOutcomeSummary);
}
