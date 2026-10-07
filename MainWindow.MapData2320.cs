using System.Windows;
using System.Windows.Controls;

namespace OrandOverlay;

public partial class MainWindow
{
    private bool UsesMap2320 => _catalog.HasModernSource;
    internal const string Map2320ReferenceWarning = "이전 안내 자료의 유닛 수치와 통계는 참고용이에요. 현재 게임의 실제 수치와 다를 수 있어요.";
    internal const string Map2320ManualNavigationNotice = "항법은 직접 선택해 주세요. 자동 추천은 준비 중입니다.";

    internal static StoryProgressionProfile LoadApplicationStoryProfile(DataCatalog catalog) =>
        catalog.MapBundle is { } selected
            ? new StoryProgressionProfile(selected.Source, selected.Story.ProjectGuaranteedBaseStages())
            : catalog.OfflineBundle is { } bundle
            ? new StoryProgressionProfile(bundle.Source, bundle.Story.ProjectGuaranteedBaseStages())
            : MapStoryProfileLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Data"));

    internal static ClearBuildStats? RankingClearStats(DataCatalog catalog, ClearBuildStats stats) =>
        catalog.HasModernSource ? null : stats.HasData ? stats : null;

    internal static string VersionLabel(DataCatalog catalog, string legacySummary) =>
        catalog.HasModernSource
            ? $"원랜디 {catalog.MapVersion} · 일반 모드 조합 안내 · 유닛 수치는 이전 자료를 참고한 값이에요"
            : $"데이터 {catalog.Data.DataVersion} · {catalog.Data.Disclaimer}" + legacySummary;

    private string ApplicationDataVersionLabel() => VersionLabel(_catalog, ClearStatsSummary());

    internal static IReadOnlyList<NavigationOption> ModernNavigationOptions(Map2320DataBundle bundle) => MapNavigationCatalog.Options(bundle);
    internal static NavigationOption UnselectedNavigation(string categoryId = "AlliedForces") => MapNavigationCatalog.Unselected(categoryId);
    internal static NavigationOption ResolveVersionNavigation(DataCatalog catalog, string? id) => MapNavigationCatalog.Resolve(catalog, id);
    private NavigationOption ResolveApplicationNavigation(string? id) => ResolveVersionNavigation(_catalog, id);
    internal static IReadOnlyList<NavigationOption> VersionNavigationsForCategory(DataCatalog catalog, string categoryId) => MapNavigationCatalog.ForCategory(catalog, categoryId);
    private IReadOnlyList<NavigationOption> ApplicationNavigationsForCategory(string categoryId) => VersionNavigationsForCategory(_catalog, categoryId);

    private void ConfigureMap2320Navigation()
    {
        if (!UsesMap2320) return;
        // Disable only the in-memory automatic mode. Do not migrate stored navigation identities.
        _settings.AutoRecommendNavigation = false;
        var updating = _updatingSelections;
        _updatingSelections = true;
        try { AutoNavigationCheck.IsChecked = false; }
        finally { _updatingSelections = updating; }
        AutoNavigationCheck.IsEnabled = false;
        AutoNavigationCheck.ToolTip = Map2320ManualNavigationNotice;
        ToolTipService.SetShowOnDisabled(AutoNavigationCheck, true);
        NavigationLabel.Text = "항법 선택";
        NavigationLabel.Visibility = Visibility.Visible;
        NavigationSelectRow.Visibility = Visibility.Visible;
    }

    internal static string ModernNavigationDisplay(NavigationOption selected) =>
        selected.Id == "Unselected"
            ? "항법을 아직 고르지 않았어요. 게임에서 선택한 항법도 확인되지 않았어요."
            : $"직접 고른 항법: {selected.Name} · 게임에서 선택했는지는 확인하지 못했어요. 자동 추천 점수는 아직 알 수 없어요.";

    private void RenderMap2320NavigationContext()
    {
        ConfigureMap2320Navigation();
        var selected = NavigationCombo.SelectedItem as NavigationOption ?? UnselectedNavigation();
        NavigationStatusText.Text = Map2320ManualNavigationNotice;
        NavigationCandidateText.Text = ModernNavigationDisplay(selected);
        ConfirmNavigationButton.Visibility = Visibility.Collapsed;
        _overlay.RenderNavigationContext(Map2320ManualNavigationNotice,
            $"직접 고른 항법: {selected.Name} · 게임에서 실제로 선택했는지 확인해 주세요");
    }
}
