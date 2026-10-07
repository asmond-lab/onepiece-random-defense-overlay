namespace OrandOverlay;

public static class AutomaticNavigationAdvisor
{
    public static NavigationAdvice? Select(DataCatalog catalog, IReadOnlyList<InventoryEntry> inventory,
        IReadOnlyList<UnitDefinition> goals, int round, int plannedTopCount,
        NavigationIntervalScoringResult? scored)
    {
        if (Map2320DataBundle.IsCompatible(catalog.MapVersion) || !inventory.Any(entry => entry.Count > 0)) return null;
        bool Compatible(string id) => NavigationProfiles.Find(id).TopUnitLimit >= plannedTopCount;
        if (scored?.RecommendedOptionId is { } id && Compatible(id) &&
            scored.State is NavigationRecommendationState.Provisional or NavigationRecommendationState.Actionable or
                NavigationRecommendationState.Locked)
            return new NavigationAdvice(id, NavigationProfiles.Find(id).Name,
                "현재 패·목표 묶음·항법 결과 구간을 비교한 자동 추천", [])
                { CanSelectNow = round is >= 21 and <= 23 };
        var candidate = new NavigationAdvisor(catalog)
            .EvaluateGoals(inventory, goals, round, NavigationProfiles.Options.Count, selectionWindow: true)
            .FirstOrDefault(item => Compatible(item.OptionId));
        return (candidate ?? new NavigationAdvice("AlliedForces.DoubleBenefit", "일석이조",
            "추가 우세 근거가 부족해 상위 수 제한과 충돌하지 않는 기본 항법을 잠정 추천", []))
            with { CanSelectNow = round is >= 21 and <= 23 };
    }
}
