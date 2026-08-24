namespace OrandOverlay;

/// <summary>
/// 목표 유닛의 조합 트리 생성·남은 제작 단계 계산·조합식 등급 조회 전담 빌더.
/// RecommendationEngine에서 동작 보존으로 추출된 클래스 — 로직 변경 금지,
/// 조정은 RecommendationEngine 쪽 호출 지점에서만 한다.
/// </summary>
internal sealed class RecipeTreeBuilder(DataCatalog catalog, CombineHotkeyCatalog? combineHotkeys)
{
    public IReadOnlyList<string> RecipeLegendaryUnitIds(string goalUnitId) =>
        RecipeLegendaryIds(catalog.Unit(goalUnitId));

    public IReadOnlyList<string> RecipeSpecialUnitIds(string unitId) =>
        RecipeTierIds(catalog.Unit(unitId), "특별함");

    /// <summary>
    /// 초월 조합식의 전설급 직접 재료. 전설이 없으면 히든을 쓴다.
    /// 스토리 진행을 위해 후보 보드에서 역할 패키지보다 앞에 둔다.
    /// </summary>
    public List<string> RecipeLegendaryIds(UnitDefinition goal)
    {
        if (BaseTier(goal.Tier) != "초월") return [];
        var legends = DirectRecipeIds(goal, "전설");
        return legends.Count > 0 ? legends : DirectRecipeIds(goal, "히든");
    }

    // 목표 조합 트리 안에 있는 희귀함 유닛들. 하위 희귀함의 재료 트리까지는
    // 내려가지 않는다(그 하위는 캐스케이드 클릭으로 확인).
    public HashSet<string> RareShipTreeIds(UnitDefinition goal)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(UnitDefinition unit, HashSet<string> visiting)
        {
            if (!visiting.Add(unit.Id)) return;
            foreach (var childId in unit.Recipe.Keys)
            {
                var child = catalog.Unit(childId);
                if (IsResourcePseudo(child) || string.Equals(child.Id, goal.Id,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (BaseTier(child.Tier) == "희귀함")
                {
                    found.Add(child.Id);
                    continue;
                }
                Walk(child, visiting);
            }
        }
        Walk(goal, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return found;
    }

    public List<string> DirectRecipeIds(UnitDefinition root, string tier) =>
        root.Recipe.Keys
            .Select(catalog.Unit)
            .Where(unit => BaseTier(unit.Tier) == tier)
            .Select(unit => unit.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public List<string> RecipeTierIds(UnitDefinition root, string tier)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string unitId)
        {
            if (!seen.Add(unitId)) return;
            var unit = catalog.Unit(unitId);
            if (BaseTier(unit.Tier) == tier)
                ids.Add(unit.Id);
            foreach (var childId in unit.Recipe.Keys)
                Visit(childId);
        }
        foreach (var childId in root.Recipe.Keys)
            Visit(childId);
        return ids;
    }

    public RecipeTreeNode BuildRecipeTree(string unitId, int requiredCount,
        IDictionary<string, int> availability, HashSet<string> visiting)
    {
        var unit = catalog.Unit(unitId);
        var available = Math.Max(0, availability.TryGetValue(unit.Id, out var count) ? count : 0);
        var owned = Math.Min(requiredCount, available);
        availability[unit.Id] = available - owned;
        var remaining = requiredCount - owned;
        var children = new List<RecipeTreeNode>();
        if (remaining > 0 && visiting.Add(unitId))
        {
            foreach (var (childId, childCount) in unit.Recipe
                         .Where(pair => pair.Value > 0)
                         .Where(pair => !IsResourcePseudo(catalog.Unit(pair.Key))))
            {
                var total = childCount > int.MaxValue / Math.Max(1, remaining)
                    ? int.MaxValue
                    : childCount * remaining;
                children.Add(BuildRecipeTree(childId, total, availability, visiting));
            }
            visiting.Remove(unitId);
        }

        return new RecipeTreeNode
        {
            UnitId = unit.Id,
            Name = unit.Name,
            Tier = unit.Tier,
            Image = unit.Image,
            RequiredCount = requiredCount,
            OwnedCount = owned,
            Children = children
        };
    }

    public List<RecipeCraftStep> BuildRemainingCraftSteps(RecipeTreeNode root,
        IReadOnlyDictionary<string, int> inventory, RecipeCompletionCalculator calculator)
    {
        var totals = new Dictionary<string, (RecipeTreeNode Node, long Required, long Owned)>(
            StringComparer.OrdinalIgnoreCase);
        var ingredientTotals = new Dictionary<string,
            Dictionary<string, (RecipeTreeNode Node, long Required, int SelectionOrder)>>(StringComparer.OrdinalIgnoreCase);
        // 목표 자신의 최종 조합도 하나의 단계다 — 재료가 다 모였을 때 "어떤 유닛을
        // 선택해 무슨 키를 누르는지"까지 카드에서 보이게 루트부터 방문한다(유저 요청).
        Visit(root);

        return totals.Values
            .Select(value => new RecipeCraftStep
            {
                UnitId = value.Node.UnitId,
                Name = value.Node.Name,
                Tier = value.Node.Tier,
                Image = value.Node.Image,
                RequiredCount = (int)Math.Min(int.MaxValue, value.Required),
                OwnedCount = (int)Math.Min(int.MaxValue, value.Owned),
                CombineKey = combineHotkeys
                    ?.FindByResult(catalog.Unit(value.Node.UnitId).Rawcodes)?.Key,
                CombineCommands = catalog.Unit(value.Node.UnitId).CombineCommands,
                // 남은 수량 기준 재료 완성률 — 드릴다운에서 하위 단계 %로 보여준다.
                CompletionRatio = calculator.Calculate(
                    Enumerable.Repeat(value.Node.UnitId,
                        (int)Math.Clamp(value.Required - value.Owned, 1, 50)),
                    inventory).CompletionRatio,
                Ingredients = ingredientTotals.GetValueOrDefault(value.Node.UnitId)?.Values
                    .Select(ingredient => new RecipeCraftIngredient
                    {
                        UnitId = ingredient.Node.UnitId,
                        Name = ingredient.Node.Name,
                        Tier = ingredient.Node.Tier,
                        RequiredCount = (int)Math.Min(int.MaxValue, ingredient.Required),
                        OwnedCount = ingredient.Node.OwnedCount,
                        SelectionOrder = ingredient.SelectionOrder
                    })
                    .OrderBy(ingredient => ingredient.SelectionOrder)
                    .ToList() ?? []
            })
            .Where(step => step.MissingCount > 0)
            .OrderBy(step => CraftTierOrder(step.Tier))
            .ThenBy(step => step.Name, StringComparer.CurrentCulture)
            .ToList();

        void Visit(RecipeTreeNode node)
        {
            var tierOrder = CraftTierOrder(node.Tier);
            // 안흔함도 실제로 흔함 패를 선택해 조합하는 단계다. 최하위 재료 목록으로만
            // 남기지 말고 티모지지처럼 안흔함부터 모든 조합 단계를 보여준다.
            if (node.Children.Count > 0 && tierOrder >= 1 && node.OwnedCount < node.RequiredCount)
            {
                if (totals.TryGetValue(node.UnitId, out var current))
                    totals[node.UnitId] = (current.Node,
                        Math.Min(int.MaxValue, current.Required + node.RequiredCount),
                        Math.Min(int.MaxValue, current.Owned + node.OwnedCount));
                else
                    totals[node.UnitId] = (node, node.RequiredCount, node.OwnedCount);

                if (!ingredientTotals.TryGetValue(node.UnitId, out var ingredients))
                {
                    ingredients = new Dictionary<string, (RecipeTreeNode Node, long Required, int SelectionOrder)>(
                        StringComparer.OrdinalIgnoreCase);
                    ingredientTotals[node.UnitId] = ingredients;
                }
                for (var selectionOrder = 0; selectionOrder < node.Children.Count; selectionOrder++)
                {
                    var ingredient = node.Children[selectionOrder];
                    if (ingredients.TryGetValue(ingredient.UnitId, out var currentIngredient))
                        ingredients[ingredient.UnitId] = (currentIngredient.Node,
                            Math.Min(int.MaxValue, currentIngredient.Required + ingredient.RequiredCount),
                            Math.Min(currentIngredient.SelectionOrder, selectionOrder));
                    else
                        ingredients[ingredient.UnitId] = (ingredient, ingredient.RequiredCount, selectionOrder);
                }
            }
            foreach (var child in node.Children) Visit(child);
        }
    }

    public static int CraftTierOrder(string tier)
    {
        var baseTier = tier.Split('[', 2)[0].Trim();
        return baseTier switch
        {
            "흔함" => 0,
            "안흔함" => 1,
            "특별함" => 2,
            "희귀함" => 3,
            "신비함" => 4,
            "전설" => 5,
            "히든" => 6,
            "변화된" => 7,
            "왜곡됨" => 8,
            "초월" => 9,
            "불멸" => 10,
            "영원" => 11,
            "제한됨" => 12,
            _ => 4
        };
    }

    private static bool IsResourcePseudo(UnitDefinition unit) =>
        unit.Tier.Equals("자원", StringComparison.OrdinalIgnoreCase) ||
        unit.Rawcodes.Any(rawcode => rawcode is "GOLD" or "LUMBER" or "POINT" or "RANDOM");

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}
