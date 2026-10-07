using System.Collections.Immutable;

namespace OrandOverlay;

public sealed record RouteQuestEvaluation(int PlannedTopCount, int FutureCommonWisps,
    int SurplusCommonWisps, int FutureBuildUpperBp, string Description)
{
    public int PlannedBuildBp { get; init; }
    public long PlannedMissingLeaves { get; init; }
    public static RouteQuestEvaluation Evaluate(AdaptivePlanningInputSource source)
    {
        var counts = source.Inventory.Where(x => x.Count > 0)
            .GroupBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count), StringComparer.OrdinalIgnoreCase);
        var goals = source.PlannedGoalUnitIds.IsDefaultOrEmpty
            ? new List<string> { source.GoalUnitId }
            : source.PlannedGoalUnitIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (goals.Count > 2 || goals.Any(id => !source.Units.ContainsKey(id)))
            throw new ArgumentException("The explicit goal bundle must contain at most two known units.", nameof(source));
        var calculator = new RecipeCompletionCalculator(id => source.Units[id]);
        var notes = new List<string> { source.RouteQuests.Describe() };
        var rewards = 0;
        foreach (var quest in RouteQuestCatalog.All.Where(x => x.CraftTier is not null))
        {
            var tier = quest.CraftTier!;
            var status = source.RouteQuests.Status(quest.Id);
            var amount = quest.CommonSelectionWisps;
            if (status != RouteQuestStatus.Active)
                continue;
            var target = goals.FirstOrDefault(id => Tier(source.Units[id]) == tier);
            if (target is not null && counts.GetValueOrDefault(target) > 0)
            {
                notes.Add($"{quest.Name}: 목표 이미 보유 · 새 조합 계획이 없어 보상 미산입");
                continue;
            }
            if (target is null && source.PlannedGoalUnitIds.IsDefaultOrEmpty &&
                source.PursueBothRouteQuests && tier is "초월" or "제한됨")
            {
                target = source.Units.Values.Where(unit => Tier(unit) == tier && unit.Recipe.Count > 0 &&
                        counts.GetValueOrDefault(unit.Id) == 0)
                    .OrderBy(unit => Missing(calculator.CalculateAllocation(
                        goals.Append(unit.Id), counts).Progress))
                    .ThenBy(unit => unit.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
                if (target is not null) goals.Add(target);
            }
            if (target is null)
            {
                notes.Add($"{quest.Name}: 현재 목표 밖, 보상 미산입");
                continue;
            }
            rewards += amount;
            notes.Add($"{quest.Name}: {source.Units[target].Name} 조합 후 흔함선택위습 {amount}개 (1회)");
        }
        var allocation = calculator.CalculateAllocation(goals, counts);
        var missing = Missing(allocation.Progress);
        var hasOtherCost = allocation.ResourceRequirements.Values.Any(value => value > 0) ||
            allocation.Progress.MissingLeaves.Any(leaf => leaf.Tier.Split('[', 2)[0].Trim() != "흔함");
        var surplus = hasOtherCost ? 0 : (int)Math.Max(0, rewards - missing);
        var upper = (int)Math.Min(10_000, surplus * 10_000L /
            Math.Max(1, allocation.Progress.RequiredLeafCount));
        var topIds = source.CompletedTopUnitIds.Concat(counts.Keys.Where(id =>
            source.Units.TryGetValue(id, out var unit) && IsTop(unit))).Concat(goals.Where(id =>
            IsTop(source.Units[id]))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var topCount = topIds.Sum(id => Math.Max(1, counts.GetValueOrDefault(id)));
        notes.Add($"목표 묶음: 최상위 {topCount}기 · 부족 재료 {missing}개 · " +
            $"총 조합 비용 골드 {allocation.ResourceRequirements.Gold}, 목재 {allocation.ResourceRequirements.Lumber}");
        notes.Add("보상은 조합 이후 지급: 현재 패에 선지급하지 않음. 미래 잉여만 점수 상한에 반영.");
        return new(topCount, rewards, surplus, upper, string.Join("\n", notes))
        {
            PlannedBuildBp = (int)Math.Round(allocation.Progress.CompletionRatio * 10_000,
                MidpointRounding.AwayFromZero),
            PlannedMissingLeaves = missing
        };
    }

    public ImmutableArray<NavigationIntervalOptionInput> Apply(
        ImmutableArray<NavigationIntervalOptionInput> options) => options.Select(option =>
    {
        var limit = NavigationProfiles.Find(option.OptionId).TopUnitLimit;
        var compatible = PlannedTopCount <= limit;
        var scenarios = option.CoupledScenarios;
        if (compatible && FutureBuildUpperBp > 0)
            scenarios = scenarios.AddRange(scenarios.Select(scenario => scenario with
            {
                Id = scenario.Id + ":quest-future-upper",
                Outcomes = scenario.Outcomes.Select(outcome => outcome with
                {
                    AfterBuildBp = Math.Min(10_000, outcome.AfterBuildBp + FutureBuildUpperBp)
                }).ToImmutableArray()
            }));
        return option with
        {
            TopCompatible = option.TopCompatible && compatible,
            CoupledScenarios = scenarios,
            SourceReasonIds = option.SourceReasonIds.IsDefault ? [Description] :
                option.SourceReasonIds.Add(Description)
        };
    }).ToImmutableArray();

    private static long Missing(RecipeAllocationProgress progress) =>
        Math.Max(0, progress.RequiredLeafCount - progress.OwnedLeafCount);
    private static string Tier(UnitDefinition unit) => unit.Tier.Split('[', 2)[0].Trim();
    private static bool IsTop(UnitDefinition unit) => Tier(unit) is
        "초월" or "제한됨" or "영원" or "불멸" or "신비함";
}
