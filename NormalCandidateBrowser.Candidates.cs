namespace OrandOverlay;

public sealed partial class NormalCandidateBrowser
{
    private NormalCandidate Candidate(UnitDefinition unit)
    {
        if (_candidateCache.TryGetValue(unit.Id, out var cached)) return cached;
        var baseCandidate = CalculateCandidate(unit);
        var guide = _guide.GetValueOrDefault(unit.Id);
        var observedCount = _lastObserved.GetValueOrDefault(unit.Id);
        var authorPair = Stage == NormalCandidateStage.Utility && IsRecommendedPartner(unit) && observedCount == 0;
        var useful = authorPair;
        var reason = authorPair ? $"{_units[FirstUpperId!].Name} 추천 조합" : "";
        var candidate = baseCandidate with
        {
            StoryFast = guide?.StoryFast == true, DamageType = DamageTypeFor(unit.Id),
            MovementRoles = guide?.MovementRoles ?? [], ObservedCount = observedCount,
            UsefulSupport = useful, RecommendationReason = reason
        };
        _candidateCache[unit.Id] = candidate;
        return candidate;
    }
    private static bool HasPositiveRoleEvidence(NormalUtilityRole role) =>
        role.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        (double.TryParse(role.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) && value > 0);
    private bool IsRecommendedPartner(UnitDefinition unit) =>
        FirstUpperId is { } anchor && !unit.Id.Equals(anchor, StringComparison.OrdinalIgnoreCase) &&
        IsSupportGrade(unit) &&
        _guide.GetValueOrDefault(anchor)?.RecommendedPartners.Contains(unit.Id, StringComparer.OrdinalIgnoreCase) == true;
    private IReadOnlyList<string> SupportDeficits(string id)
    {
        var direction = Snapshot.Direction;
        var covered = _lastObserved.Keys
            .SelectMany(owned => RolesFor(owned).Where(HasPositiveRoleEvidence).Select(role => SupportCategory(role.Category))).ToHashSet(StringComparer.Ordinal);
        var absentRoles = (Compatible(id, direction) ? RolesFor(id) : []).Where(HasPositiveRoleEvidence).Select(role => SupportCategory(role.Category)).Where(category =>
            SupportNames(direction).Contains(category) && !covered.Contains(category));
        var absentMovement = (_guide.GetValueOrDefault(id)?.MovementRoles ?? []).Where(role =>
            MovementCoverage.Any(coverage => coverage.Role == role && !coverage.Covered));
        return absentRoles.Concat(absentMovement).Distinct(StringComparer.Ordinal).ToArray();
    }
    private NormalCandidate CalculateCandidate(UnitDefinition unit)
    {
        var current = Snapshot.IsCurrent;
        var observed = current && _lastObserved.GetValueOrDefault(unit.Id) > 0;
        if (!current) return new(unit, null, "현재 패 미확인", false);
        var choiceRequired = false;
        if (unit.Recipe.Count == 0 || !ValidGraph(unit.Id, new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), out choiceRequired))
            return new(unit, null, choiceRequired ? "선택형 재료가 포함되어 재료 비율을 계산하지 못해요." : "조합 재료 근거 미확인",
                !IsDiagnosticReference && observed);
        try
        {
            var allocation = _calculator.CalculateAllocation([unit.Id], _lastObserved);
            return allocation.Progress.RequiredLeafCount > 0
                ? new(unit, allocation, IsDiagnosticReference ? "인식한 유닛 기준 재료 비율 · 조합 가능 여부는 게임에서 확인해 주세요." : "재료 확보율 · 조합 조건 별도 확인", !IsDiagnosticReference && observed)
                    { RecipeStepCount = RemainingRecipeSteps(unit.Id) }
                : new(unit, null, "카드 재료 계산 대상 없음", !IsDiagnosticReference && observed);
        }
        catch (Exception e) when (e is OverflowException or ArgumentException or InvalidOperationException)
        { return new(unit, null, "재료 계산 근거 미확인", false); }
    }
    private long RemainingRecipeSteps(string target)
    {
        var available = _lastObserved.ToDictionary(p => p.Key, p => (long)p.Value, StringComparer.OrdinalIgnoreCase);
        long Visit(string id, long required)
        {
            if (ResourceKey(id) is not null) return 0;
            var owned = Math.Min(required, available.GetValueOrDefault(id));
            available[id] = available.GetValueOrDefault(id) - owned;
            var missing = required - owned;
            var unit = _units[id];
            if (missing == 0 || unit.Recipe.Count == 0) return 0;
            var steps = missing;
            foreach (var material in unit.Recipe)
                steps = checked(steps + Visit(material.Key, checked(missing * material.Value)));
            return steps;
        }
        return Visit(target, 1);
    }
}
