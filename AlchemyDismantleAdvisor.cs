namespace OrandOverlay;

/// <summary>
/// 연금술 대깨 비영에서 현재 제작 경로에 쓰지 않는 특별함만 분해 대상으로 고른다.
/// 비영 완성 전에는 비영 재료만, 완성 후에는 추천 선두 핵심 경로 하나의 재료만 보존한다.
/// 성장형 특별함은 제작 단계와 관계없이 보존한다.
/// </summary>
public sealed class AlchemyDismantleAdvisor(DataCatalog catalog)
{
    private static readonly HashSet<string> ExpertRoots =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "rawcode:H90H",
            "rawcode:4B0H",
            "rawcode:780h",
            "rawcode:640h",
            "mobydick"
        };

    public IReadOnlyList<SpecialDismantleAdvice> Evaluate(
        IEnumerable<InventoryEntry> inventory,
        IReadOnlyList<Recommendation> recommendations,
        UnitDefinition goal,
        string navigationMode,
        IReadOnlyCollection<string> growthUnitIds)
    {
        if (!goal.Rawcodes.Contains("750h", StringComparer.Ordinal) ||
            !navigationMode.Equals("BestHelp.Alchemy",
                StringComparison.OrdinalIgnoreCase))
            return [];

        var owned = inventory
            .Where(entry => entry.Count > 0)
            .GroupBy(entry => entry.UnitId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Count),
                StringComparer.OrdinalIgnoreCase);
        var requiredSpecials = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        var goalOwned = owned.GetValueOrDefault(goal.Id) > 0;
        if (!goalOwned)
            CollectRequiredSpecials(goal.Id, 1, owned, requiredSpecials);
        else
        {
            var immediateRootId = recommendations
                .Select(item => item.Route.GoalUnitId)
                .FirstOrDefault(ExpertRoots.Contains);
            if (immediateRootId is not null)
                CollectRequiredSpecials(immediateRootId, 1, owned, requiredSpecials);
        }

        var growth = growthUnitIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<SpecialDismantleAdvice>();
        foreach (var entry in owned.OrderBy(pair => catalog.Unit(pair.Key).Name))
        {
            var unit = catalog.Unit(entry.Key);
            if (BaseTier(unit.Tier) != "특별함") continue;
            if (growth.Contains(entry.Key))
            {
                result.Add(new SpecialDismantleAdvice(
                    entry.Key, unit.Name, false, "성장형 특별함 — 분해 금지"));
                continue;
            }

            var needed = requiredSpecials.GetValueOrDefault(entry.Key);
            var excess = Math.Max(0, entry.Value - needed);
            if (excess > 0)
            {
                var name = excess > 1 ? $"{unit.Name} ×{excess}" : unit.Name;
                var reason = needed > 0
                    ? $"핵심 재료 {needed}기 보존 · 초과 {excess}기 연금술 분해"
                    : "대깨 비영 경로 밖 — 연금술로 하위 재료 전환";
                result.Add(new SpecialDismantleAdvice(
                    entry.Key, name, true, reason));
            }
            else
            {
                result.Add(new SpecialDismantleAdvice(
                    entry.Key, unit.Name, false,
                    $"비영 핵심 조합 재료 {needed}기 — 보존"));
            }
        }
        return result;
    }

    private void CollectRequiredSpecials(
        string unitId,
        int count,
        IReadOnlyDictionary<string, int> owned,
        IDictionary<string, int> required)
    {
        var unit = catalog.Unit(unitId);
        if (BaseTier(unit.Tier) == "특별함")
        {
            required.TryGetValue(unit.Id, out var current);
            required[unit.Id] = current + count;
            return;
        }
        foreach (var ingredient in unit.Recipe)
        {
            var child = catalog.Unit(ingredient.Key);
            if (IsResource(child)) continue;
            var needed = checked(count * ingredient.Value);
            if (BaseTier(child.Tier) != "특별함" &&
                owned.GetValueOrDefault(child.Id) >= needed)
                continue;
            CollectRequiredSpecials(child.Id, needed, owned, required);
        }
    }

    private static bool IsResource(UnitDefinition unit) =>
        BaseTier(unit.Tier) == "자원" ||
        unit.Rawcodes.Any(code => code is "GOLD" or "LUMBER" or "POINT" or "RANDOM");

    private static string BaseTier(string tier) => tier.Split('[', 2)[0].Trim();
}
