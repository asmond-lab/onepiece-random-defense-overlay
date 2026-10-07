using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrandOverlay;

/// <summary>An observed unit, not an inferred random-exclusive rawcode or synthetic inventory count.</summary>
public sealed record CraftTargetInstance(string InstanceId, string AppRawcode, bool HasA800);

public sealed class NormalCraftPlanner(DataCatalog catalog)
{
    private readonly NormalCraftCommandCatalog _commands = catalog.MapBundle is { } bundle2322
        ? NormalCraftCommandCatalog.Load2322(bundle2322)
        : catalog.OfflineBundle is null ? NormalCraftCommandCatalog.Empty
        : NormalCraftCommandCatalog.LoadBundled(catalog.OfflineBundle.Recipes);

    public NormalCraftPlan Build(string goalId, IReadOnlyDictionary<string, int> inventory)
    {
        if ((catalog.MapVersion is "2.322" or "2.323") &&
            (goalId == "rawcode:2C0h" || goalId == "2C0h"))
            return Build2322Yujiro(inventory);
        var ledger = new PlanningLedger(catalog, _commands, inventory);
        return ledger.Build(goalId);
    }

    /// <summary>Offline 2.322 recipe preview. KING is not observable by this planner; never issue ready instructions.</summary>
    public NormalCraftPlan Build2322Yujiro(IReadOnlyDictionary<string, int> inventory,
        IReadOnlyList<CraftTargetInstance>? observedInstances = null)
    {
        var projection = (catalog.MapBundle ?? Map2322DataBundle.LoadBundled()).Recipes.Project("2C0h")
            ?? throw new InvalidDataException("Missing Yujiro source recipe.");
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "map-unit-additions-" + (catalog.MapVersion == Map2323SourceContract.MapVersion ? "2323" : "2322") + ".json")));
        var root = json.RootElement;
        if (root.GetProperty("mapVersion").GetString() != (catalog.MapVersion == Map2323SourceContract.MapVersion ? "2.323" : "2.322") ||
            root.GetProperty("sourceSha256").GetString() != (catalog.MapVersion == Map2323SourceContract.MapVersion ? Map2323SourceContract.JassSha256 : Map2322SourceContract.JassSha256))
            throw new InvalidDataException("Yujiro catalog source mismatch.");
        var row = root.GetProperty("units")[0];
        if (root.GetProperty("units").GetArrayLength() != 1 ||
            row.GetProperty("ingredients").GetArrayLength() != 6 ||
            row.GetProperty("conditions").GetArrayLength() != 2 ||
            row.GetProperty("conditions")[0].GetProperty("kind").GetString() != "KING" ||
            row.GetProperty("conditions")[0].GetProperty("id").GetString() != "h0C2" ||
            row.GetProperty("conditions")[1].GetProperty("kind").GetString() != "PICK" ||
            row.GetProperty("conditions")[1].GetProperty("id").GetString() != "A800" ||
            row.GetProperty("rawcode").GetString() != projection.AppRawcode ||
            row.GetProperty("sourceRawcode").GetString() != "h0C2" ||
            row.GetProperty("recipeId").GetString() != projection.RecipeId ||
            !projection.ConditionalRequirements.Select(x => x.Source.Kind + ":" + x.Source.Id)
                .SequenceEqual(new[] { "KING:h0C2", "PICK:A800" }))
            throw new InvalidDataException("Yujiro source conditions mismatch.");
        var remaining = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, count) in inventory)
        {
            // Catalog IDs and rawcode keys can describe the same physical units.
            var id = catalog.UnitsById.TryGetValue(key, out var unit) && unit.Rawcodes.Count > 0
                ? unit.Rawcodes[0]
                : key.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase) ? key[8..] : key;
            remaining[id] = Math.Max(remaining.GetValueOrDefault(id), Math.Max(0, count));
        }
        var ingredients = new List<NormalCraftIngredient>();
        var reservedInstances = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in row.GetProperty("ingredients").EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            var required = entry.GetProperty("count").GetInt32();
            var kind = entry.GetProperty("kind").GetString()!;
            if (projection.IngredientsByAppRawcode.GetValueOrDefault(id) != required ||
                (kind == "UNIT" && Map2320RecipeRegistry.ToAppRawcode(entry.GetProperty("sourceId").GetString()!) != id) ||
                (kind != "UNIT" && kind != (id == "GOLD" ? "GOLD" : "WOOD")))
                throw new InvalidDataException("Yujiro ingredient differs from map projection.");
            var resource = kind != "UNIT";
            var owned = resource ? 0 : Math.Min(required, remaining.GetValueOrDefault(id));
            if (!resource)
            {
                remaining[id] = remaining.GetValueOrDefault(id) - owned;
                foreach (var instance in (observedInstances ?? []).Where(x => x.AppRawcode == id &&
                    !string.IsNullOrWhiteSpace(x.InstanceId)).OrderBy(x => x.InstanceId, StringComparer.Ordinal).Take(owned))
                    reservedInstances.Add(instance.InstanceId);
            }
            ingredients.Add(new("rawcode:" + id, entry.TryGetProperty("name", out var label) ? label.GetString()! : id,
                resource ? "자원" : "재료", required, owned, 0, resource));
        }
        // The instance ledger is bounded by inventory *after* exact materials are reserved.
        var choice = RecipeWildcards.SelectObservedA800(remaining,
            (observedInstances ?? []).Where(instance => !reservedInstances.Contains(instance.InstanceId)).ToArray());
        ingredients.Add(new("PICK:A800", "랜덤전용유닛 1기 (A800 대상 선택)", "선택 재료", 1,
            choice is null ? 0 : 1, 0, false));
        var conditions = new RecipeConditionResult(RecipeConditionStatus.Unknown,
            "조합 보류: KING:h0C2 항법 제한 관측 필요" + (choice is null ? " · PICK:A800 선택 대상 능력 관측 필요" : " · PICK:A800 대상 " + choice.InstanceId + " 확인됨"));
        var step = new NormalCraftStep("rawcode:2C0h", row.GetProperty("name").GetString()!, row.GetProperty("tier").GetString()!,
            "", 1, 1, ingredients.AsReadOnly(), null, "KING 조건·선택 대상 확인 전 조합 불가", null,
            Array.Empty<string>(), conditions, false, "전체 조합식 · KING 조건 미해결 · 자동 조합 보류");
        return new NormalCraftPlan(new[] { step }, ingredients.Where(item => !item.IsResource && item.MissingCount > 0)
            .Select(item => new RecipeLeafProgress { UnitId = item.UnitId, Name = item.Name, Tier = item.Tier,
                Image = "", RequiredCount = item.RequiredCount, OwnedCount = item.OwnedCount }).ToArray(),
            new ResourceRequirements(new Dictionary<string, long> { ["GOLD"] = 10000, ["LUMBER"] = 7 }), false,
            "KING:h0C2 검증과 PICK:A800 대상 선택이 관측되지 않으면 조합 준비 완료로 표시하지 않습니다.");
    }

    private sealed class PlanningLedger
    {
        private readonly DataCatalog _catalog;
        private readonly NormalCraftCommandCatalog _commands;
        private readonly Dictionary<string, string> _appIds;
        private readonly Dictionary<string, UnitDefinition> _units = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _owned = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _demand = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _crafts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _resources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, long>> _wildcards = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RecipeLeafProgress> _missing = [];
        private readonly List<string> _order = [];
        private readonly HashSet<string> _cycles = new(StringComparer.OrdinalIgnoreCase);
        private bool _overflow;

        internal PlanningLedger(DataCatalog catalog, NormalCraftCommandCatalog commands, IReadOnlyDictionary<string, int> inventory)
        {
            _catalog = catalog;
            _commands = commands;
            _appIds = catalog.UnitsById.Values.SelectMany(unit => unit.Rawcodes.Select(code => (code, unit.Id)))
                .GroupBy(pair => pair.code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.Ordinal);
            foreach (var pair in inventory.Where(pair => pair.Value > 0 && !RecipeWildcards.IsWildcard(pair.Key)))
                Add(_owned, Resolve(pair.Key).Id, pair.Value);
        }

        internal NormalCraftPlan Build(string goalId)
        {
            var goal = Resolve(goalId);
            if (_owned.GetValueOrDefault(goal.Id) > 0) return Result([], true);
            Visit(goal.Id, []);
            var initial = new Dictionary<string, long>(_owned, StringComparer.OrdinalIgnoreCase);
            _demand[goal.Id] = 1;
            foreach (var id in _order.AsEnumerable().Reverse().Where(id => !RecipeWildcards.IsWildcard(id)))
            {
                var required = _demand.GetValueOrDefault(id);
                if (required == 0) continue;
                var unit = Resolve(id);
                if (IsResource(unit)) { Add(_resources, ResourceId(unit), required); continue; }
                var owned = Take(_owned, id, required);
                var remaining = required - owned;
                if (remaining == 0) continue;
                if (_cycles.Contains(id) || Children(unit).Count == 0)
                {
                    Missing(unit, required, owned);
                    continue;
                }
                _crafts[id] = remaining;
                foreach (var pair in Children(unit)) Add(_demand, pair.Key, Multiply(pair.Value, remaining));
            }
            foreach (var id in _order.Where(RecipeWildcards.IsWildcard))
            {
                var required = _demand.GetValueOrDefault(id);
                var remaining = required;
                var allocation = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in RecipeWildcards.Candidates(id, _owned, Resolve).ToArray())
                {
                    var take = Take(_owned, candidate.Key, remaining);
                    Add(allocation, candidate.Key, take);
                    remaining -= take;
                }
                _wildcards[id] = allocation;
                if (remaining > 0) Missing(Resolve(id), required, required - remaining);
            }
            return Result(BuildSteps(initial), false);
        }

        private IReadOnlyList<NormalCraftStep> BuildSteps(Dictionary<string, long> current)
        {
            var generated = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var steps = new List<NormalCraftStep>();
            foreach (var id in CraftOrder())
            {
                var unit = Resolve(id);
                var count = _crafts[id];
                var ingredients = new List<NormalCraftIngredient>();
                foreach (var pair in Children(unit))
                {
                    var child = Resolve(pair.Key);
                    var required = Multiply(pair.Value, count);
                    var owned = 0L;
                    var previous = 0L;
                    if (RecipeWildcards.IsWildcard(pair.Key))
                    {
                        var selected = _wildcards.GetValueOrDefault(pair.Key) ?? [];
                        foreach (var candidate in selected.ToArray())
                        {
                            var take = Take(selected, candidate.Key, required - owned);
                            owned += Take(current, candidate.Key, take);
                        }
                    }
                    else if (!IsResource(child))
                    {
                        owned = Take(current, pair.Key, required);
                        previous = Take(generated, pair.Key, required - owned);
                    }
                    ingredients.Add(new(child.Id, child.Name, child.Tier, required, owned, previous, IsResource(child)));
                }
                var conditions = RecipeConditionEvaluator.Evaluate(unit);
                var commands = Map2320DataBundle.IsCompatible(_catalog.MapVersion) && _catalog.OfflineBundle is not null
                    ? unit.CombineCommands.ToArray() : [];
                var key = Map2320DataBundle.IsCompatible(_catalog.MapVersion) && _catalog.OfflineBundle is not null ? _commands.Find(unit) : null;
                var selection = key is null ? null : Resolve("rawcode:" + key.SelectionRawcode);
                var ready = ingredients.Where(item => !item.IsResource).All(item => item.MissingCount == 0);
                var status = !ready ? "재료 부족 · 확보 후 조합" : ingredients.Any(item => item.PriorStepCount > 0)
                    ? "선행 단계 완료 후 조합" : "현재 카드 재료 충족";
                if (!conditions.IsSatisfied || ingredients.Any(item => item.IsResource))
                    status += " · 추가 조건·자원 확인 필요";
                if (commands.Length == 0 && key is null) status += " · 단축키·명령 미확인";
                steps.Add(new(unit.Id, unit.Name, unit.Tier, unit.Image, count, count,
                    ingredients.AsReadOnly(), selection?.Id, selection?.Name ?? (commands.Length > 0 ? "채팅 조합 · 선택 조건 미확인" : "선택 유닛 미확인"),
                    key?.Key, Array.AsReadOnly(commands), conditions, ready, status));
                Add(generated, id, count);
            }
            return steps.AsReadOnly();
        }

        private IEnumerable<string> CraftOrder()
        {
            var remaining = _crafts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            while (remaining.Count > 0)
            {
                var next = remaining.Where(id => !Children(Resolve(id)).Keys.Any(remaining.Contains))
                    .OrderBy(id => RecipeTreeBuilder.CraftTierOrder(Resolve(id).Tier))
                    .ThenBy(id => id, StringComparer.Ordinal).First();
                remaining.Remove(next);
                yield return next;
            }
        }

        private NormalCraftPlan Result(IReadOnlyList<NormalCraftStep> steps, bool owned)
        {
            var caveat = "제작 순서 안내입니다. 선행 단계의 결과는 예상 수량이며, 실제 보유 수량이 바뀌면 다시 계산합니다.";
            if (steps.Count > 0) caveat += " 게임 내 조건을 확인하세요. 2.320 단축키·선택 유닛 근거가 없는 단계는 미확인으로 표시합니다.";
            if (_cycles.Any(id => _demand.GetValueOrDefault(id) > 0)) caveat += " 순환 조합은 펼치지 않았습니다. 해당 재료를 직접 확보해야 합니다.";
            if (_overflow) caveat += " 수량이 계산 범위를 초과하여 일부 값은 상한으로 표시합니다.";
            return new(steps, _missing.AsReadOnly(), new ResourceRequirements(_resources), owned, caveat);
        }

        private void Visit(string id, List<string> path)
        {
            var cycleStart = path.FindIndex(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (cycleStart >= 0)
            {
                foreach (var member in path.Skip(cycleStart)) _cycles.Add(member);
                return;
            }
            if (_order.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
            path.Add(id);
            if (!RecipeWildcards.IsWildcard(id))
                foreach (var child in Children(Resolve(id))) Visit(child.Key, path);
            path.RemoveAt(path.Count - 1);
            _order.Add(id);
        }

        private Dictionary<string, long> Children(UnitDefinition unit)
        {
            var children = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in unit.Recipe.Where(pair => pair.Value > 0)
                         .OrderBy(pair => RecipeWildcards.IsWildcard(pair.Key) ? 1 : 0)
                         .ThenBy(pair => pair.Key, StringComparer.Ordinal))
                Add(children, Resolve(pair.Key).Id, pair.Value);
            return children;
        }

        private UnitDefinition Resolve(string id)
        {
            if (id.StartsWith("rawcode:", StringComparison.OrdinalIgnoreCase))
                id = _appIds.GetValueOrDefault(id[8..], id);
            if (_units.TryGetValue(id, out var existing)) return existing;
            var unit = _catalog.Unit(id);
            _units[id] = unit;
            return unit;
        }

        private void Missing(UnitDefinition unit, long required, long owned) => _missing.Add(new()
        {
            UnitId = unit.Id, Name = unit.Name, Tier = unit.Tier, Image = unit.Image,
            RequiredCount = required, OwnedCount = owned
        });

        private static long Take(Dictionary<string, long> values, string id, long demand)
        {
            var take = Math.Min(demand, values.GetValueOrDefault(id));
            values[id] = values.GetValueOrDefault(id) - take;
            return take;
        }

        private void Add(Dictionary<string, long> values, string id, long count)
        {
            var value = values.GetValueOrDefault(id);
            if (value > long.MaxValue - count) { _overflow = true; values[id] = long.MaxValue; }
            else values[id] = value + count;
        }

        private long Multiply(long left, long right)
        {
            if (left > long.MaxValue / right) { _overflow = true; return long.MaxValue; }
            return left * right;
        }

        private static string ResourceId(UnitDefinition unit) =>
            (unit.Rawcodes.FirstOrDefault() ?? unit.Id.Replace("rawcode:", "", StringComparison.OrdinalIgnoreCase)).ToUpperInvariant();
        private static bool IsResource(UnitDefinition unit) => unit.Tier == "자원" ||
            ResourceId(unit) is "GOLD" or "LUMBER" or "POINT" or "RANDOM";
    }
}
