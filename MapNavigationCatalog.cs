namespace OrandOverlay;

/// <summary>Version-bound planning identity, never an observation of the player's chosen navigation.</summary>
public static class MapNavigationCatalog
{
    public static IReadOnlyList<NavigationOption> Options(Map2320DataBundle bundle) =>
        bundle.Navigation.Options.Select(option =>
        {
            var categoryId = option.Id.Split('.')[0];
            var category = NavigationProfiles.Categories.Single(x => x.Id == categoryId);
            return new NavigationOption(option.Id, categoryId, category.Name, option.Name,
                option.Id == "PathOfKings.MartialLaw" ? 0 : categoryId == "PathOfKings" ? 1 : int.MaxValue,
                string.Join(" · ", option.Effects) + " · 자동 점수 미확인 · 원문 수동 계획");
        }).ToList();

    public static IReadOnlyList<NavigationOption> Options(Map2322DataBundle bundle) =>
        bundle.Navigation.Options.Select(option =>
        {
            var category = NavigationProfiles.Categories.Single(x => x.Id == option.Category);
            return new NavigationOption(option.Id, category.Id, category.Name, option.Name,
                option.Id == "PathOfKings.MartialLaw" ? 0 : category.Id == "PathOfKings" ? 1 : int.MaxValue,
                $"{bundle.MapVersion} 원문 기준 · 자동 점수 미확인 · 수동 계획");
        }).ToList();

    public static NavigationOption Unselected(string categoryId = "AlliedForces")
    {
        var category = NavigationProfiles.Categories.FirstOrDefault(x => x.Id == categoryId)
            ?? NavigationProfiles.Categories[0];
        return new("Unselected", category.Id, category.Name, "항법 미선택", int.MaxValue,
            "계획 미선택 · 실제 항법 관측 아님 · 항법 전용 효과 미적용");
    }

    public static NavigationOption Resolve(DataCatalog catalog, string? id) =>
        catalog.MapBundle is { } current
            ? Options(current).FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal)) ?? Unselected()
            : catalog.OfflineBundle is { } bundle
            ? Options(bundle).FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal)) ?? Unselected()
            : NavigationProfiles.Find(id);

    public static IReadOnlyList<NavigationOption> ForCategory(DataCatalog catalog, string categoryId) =>
        catalog.MapBundle is { } current
            ? new[] { Unselected(categoryId) }.Concat(Options(current).Where(x => x.CategoryId == categoryId)).ToList()
            : catalog.OfflineBundle is { } bundle
            ? new[] { Unselected(categoryId) }.Concat(Options(bundle).Where(x => x.CategoryId == categoryId)).ToList()
            : NavigationProfiles.ForCategory(categoryId);
}
