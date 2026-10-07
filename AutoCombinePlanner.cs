namespace OrandOverlay;

/// <summary>
/// 현재 패에서 지금 바로 실행할 수 있는 조합 단계를 순서대로 계산한다.
/// 각 단계는 "어느 재료 유닛(트리거)을 선택해 어떤 키를 누르는지"까지 결정한다.
/// 실제 입력 전송은 실행기(AutoCombineService)가 담당하고, 이 클래스는 순수 계산만 한다.
/// </summary>
public sealed class AutoCombinePlanner(DataCatalog catalog, CombineHotkeyCatalog hotkeys)
{
    public IReadOnlyList<AutoCombineStep> Plan(
        IReadOnlyList<Recommendation> recommendations,
        IEnumerable<InventoryEntry> inventory,
        IEnumerable<string>? completedUnitIds = null,
        IEnumerable<string>? protectedUnitIds = null,
        int? topUnitLimit = null,
        RecipeConditionContext? conditionContext = null)
    {
        if (!hotkeys.HasData || recommendations.Count == 0) return [];

        var owned = inventory
            .Where(entry => entry.Count > 0 || IsResource(entry.UnitId))
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(
            completedUnitIds ?? [], StringComparer.OrdinalIgnoreCase);

        var steps = new List<AutoCombineStep>();
        var virtualOwned = new Dictionary<string, int>(owned, StringComparer.OrdinalIgnoreCase);
        var combatInventory = new Dictionary<string, int>(owned, StringComparer.OrdinalIgnoreCase);
        foreach (var id in (protectedUnitIds ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
            if (virtualOwned.GetValueOrDefault(id) > 0) virtualOwned[id]--;
        var calculator = new RecipeCompletionCalculator(catalog.Unit);
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plannedTokens = new HashSet<string>(StringComparer.Ordinal);
        long plannedLumber = 0;

        // 추천 순서대로, 하위 단계부터(RemainingCraftSteps가 이미 제작 순서) 지금
        // 재료가 전부 있는 단계만 담는다. 상위 단계는 하위가 완성되면 다음 계획에서 잡힌다.
        foreach (var recommendation in recommendations)
        {
            if (completed.Contains(recommendation.Route.GoalUnitId)) continue;
            if (recommendation.MissingSpecials.Count > 0) continue;
            if (recommendation.GuidePlan is { } reservationPlan)
                virtualOwned = BulletGuideReservations.Available(catalog, combatInventory,
                    reservationPlan.ProtectedUnitIds.Concat(protectedUnitIds ?? []), recommendation.Route.GoalUnitId);
            // RemainingCraftSteps는 중간 단계만 담으므로 추천 유닛 자체(루트) 조합
            // 단계를 합성해 함께 검사한다.
            var rootStep = SynthesizeRootStep(recommendation);
            var craftSteps = rootStep is null
                ? recommendation.RemainingCraftSteps
                : recommendation.RemainingCraftSteps.Append(rootStep).ToList();
            foreach (var step in craftSteps)
            {
                if (step.MissingCount <= 0) continue;
                if (completed.Contains(step.UnitId)) continue;
                if (step.UnitId.Equals(recommendation.Route.GoalUnitId, StringComparison.OrdinalIgnoreCase) &&
                    virtualOwned.GetValueOrDefault(step.UnitId) > 0) continue;
                if (!planned.Add(step.UnitId)) continue;
                if (step.Ingredients.Count == 0) continue;
                // Tree quantities cover every missing copy; one input action crafts only one.
                // An owned intermediate may already be allocated to another recipe branch.
                var ingredients = catalog.Unit(step.UnitId).Recipe.Select(pair => new RecipeCraftIngredient
                {
                    UnitId = pair.Key,
                    Name = catalog.Unit(pair.Key).Name,
                    RequiredCount = pair.Value
                }).ToList();
                // 목재 같은 자원은 패 인식 대상이 아니므로 보유 조건에서 제외한다.
                var materialIngredients = ingredients
                    .Where(ingredient => !IsResource(ingredient.UnitId))
                    .ToList();
                if (materialIngredients.Count == 0) continue;
                var actionUnit = catalog.Unit(step.UnitId);
                if (!RecipeConditionEvaluator.Evaluate(actionUnit, conditionContext).IsSatisfied) continue;
                var recipeLumber = ingredients.Where(ingredient => IsLumber(ingredient.UnitId))
                    .Sum(ingredient => (long)Math.Max(0, ingredient.RequiredCount));
                // The condition describes the same recipe cost, not an additional charge.
                var lumberCost = Math.Max(recipeLumber, actionUnit.RecipeConditions?.Lumber ?? 0);
                if (conditionContext?.Observation?.Lumber is { } knownLumber &&
                    plannedLumber + lumberCost > knownLumber) continue;
                if (actionUnit.RecipeConditions is { TokenCount: > 0, TokenId: { } tokenId } &&
                    plannedTokens.Contains(tokenId)) continue;
                var direct = RecipeWildcards.AllocateDirect(actionUnit, virtualOwned, catalog.Unit);
                if (direct is null || direct.Count == 0) continue;
                var allocation = calculator.CalculateAllocation(direct.SelectMany(pair =>
                    Enumerable.Repeat(pair.Key, checked((int)pair.Value))), virtualOwned);
                if (allocation.Progress.MissingLeaves.Count > 0) continue;
                if (topUnitLimit is { } limit && TopGradePolicy.IsTopGrade(catalog.Unit(step.UnitId).Tier))
                {
                    var remainingTops = combatInventory.Where(pair =>
                            TopGradePolicy.IsTopGrade(catalog.Unit(pair.Key).Tier))
                        .Sum(pair => pair.Value - allocation.ConsumedByUnitId.GetValueOrDefault(pair.Key));
                    if (remainingTops + 1 > limit) continue;
                }

                if (recommendation.GuidePlan is { } guide)
                {
                    if (step.UnitId == "rawcode:IC0h" && !guide.QueenConversionConfirmed) continue;
                    if (!BulletGuideCraftSafety.Allows(catalog, step.UnitId, combatInventory,
                            guide.Round, guide.ConfirmedNavigation, guide.Support?.ArmorTarget ?? 100,
                            guide.QueenConversionConfirmed, guide)) continue;
                }
                else if (recommendation.CurrentCraft is { } current)
                {
                    var action = new CurrentCraftPolicy(catalog.Unit,
                        catalog.Unit(recommendation.Route.GoalUnitId), combatInventory,
                        current.Strategy, 0, 0, calculator, conditionContext).Assess(catalog.Unit(step.UnitId));
                    if (action.LosesRequiredCombat)
                    {
                        var warning = $"조합 보류: {step.Name} — 소모되는 생존·딜 기물 먼저 대체";
                        if (!recommendation.Warnings.Contains(warning))
                            recommendation.Warnings.Add(warning);
                        continue;
                    }
                    if (!action.MaterialsReady) continue;
                }
                var resolved = Resolve(step, allocation.ConsumedByUnitId);
                if (resolved is null) continue;
                steps.Add(resolved);
                // Planning budget only: never mutate observed flags or real token state.
                if (actionUnit.RecipeConditions is { } usedConditions)
                {
                    if (usedConditions.TokenCount > 0 && usedConditions.TokenId is { } usedToken) plannedTokens.Add(usedToken);
                    
                }

                plannedLumber += lumberCost;

                // 이 단계가 소모하는 재료를 가상 차감해 같은 재료의 중복 계획을 막는다.
                foreach (var (unitId, consumed) in allocation.ConsumedByUnitId)
                {
                    virtualOwned[unitId] -= checked((int)consumed);
                    combatInventory[unitId] -= checked((int)consumed);
                }
                foreach (var resource in ingredients.Where(ingredient =>
                             IsResource(ingredient.UnitId) && combatInventory.ContainsKey(ingredient.UnitId)))
                    combatInventory[resource.UnitId] -= resource.RequiredCount;
                combatInventory[step.UnitId] = combatInventory.GetValueOrDefault(step.UnitId) + 1;
            }
        }
        return steps;
    }

    private static RecipeCraftStep? SynthesizeRootStep(Recommendation recommendation)
    {
        var root = recommendation.RecipeTree;
        if (root is null || root.OwnedCount > 0 || root.Children.Count == 0) return null;
        return new RecipeCraftStep
        {
            UnitId = root.UnitId,
            Name = root.Name,
            Tier = root.Tier,
            RequiredCount = 1,
            OwnedCount = 0,
            Ingredients = root.Children
                .Select((child, index) => new RecipeCraftIngredient
                {
                    UnitId = child.UnitId,
                    Name = child.Name,
                    Tier = child.Tier,
                    RequiredCount = child.RequiredCount,
                    OwnedCount = child.OwnedCount,
                    SelectionOrder = index
                })
                .ToList()
        };
    }

    private bool IsLumber(string unitId) => unitId is "LUMBER" or "rawcode:LUMBER" ||
        catalog.Unit(unitId).Rawcodes.Contains("LUMBER", StringComparer.Ordinal);

    private bool IsResource(string unitId)
    {
        var plain = unitId.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase)
            ? unitId["rawcode:".Length..]
            : unitId;
        if (plain is "GOLD" or "LUMBER" or "POINT" or "RANDOM") return true;
        return catalog.Unit(unitId).Tier.Split('[', 2)[0].Trim() == "자원";
    }

    private AutoCombineStep? Resolve(RecipeCraftStep step, IReadOnlyDictionary<string, long> consumed)
    {
        // 결과 유닛 rawcode로 맵 데이터의 조합 키를 찾고, 이름 표기 폴백을 둔다.
        var entry = hotkeys.FindByResult(catalog.Unit(step.UnitId).Rawcodes) ??
                    hotkeys.FindByResultName($"{step.Name} - {step.Tier}") ??
                    hotkeys.FindByResultName(step.Name);

        // 트리거(먼저 선택) 재료는 앱의 조합 트리가 이미 알고 있다. 자원은 제외한다.
        var trigger = step.Ingredients
            .Where(ingredient => !IsResource(ingredient.UnitId))
            .OrderBy(ingredient => ingredient.SelectionOrder)
            .First();
        var triggerUnit = RecipeWildcards.IsWildcard(trigger.UnitId)
            ? consumed.Keys.Select(catalog.Unit).First(unit => trigger.UnitId == RecipeWildcards.AnyNika
                ? RecipeWildcards.IsNikaAlternative(unit) : RecipeWildcards.IsSeraphim(unit))
            : catalog.Unit(trigger.UnitId);

        var commands = catalog.Unit(step.UnitId).CombineCommands;
        if (entry is null && commands.Count == 0) return null;

        return new AutoCombineStep(
            TargetUnitId: step.UnitId,
            TargetName: RecommendationPresentation.CraftUnitName(step.Name, step.Tier),
            TriggerUnitId: triggerUnit.Id,
            TriggerName: triggerUnit.Name,
            TriggerRawcode: triggerUnit.Rawcodes.FirstOrDefault() ?? "",
            Key: entry?.Key ?? "",
            Commands: commands);
    }
}

/// <summary>실행 계획 한 단계: 트리거 유닛을 선택하고 키를 누르거나 명령어를 입력한다.</summary>
public sealed record AutoCombineStep(
    string TargetUnitId,
    string TargetName,
    string TriggerUnitId,
    string TriggerName,
    string TriggerRawcode,
    string Key,
    IReadOnlyList<string> Commands);
