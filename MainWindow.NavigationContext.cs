using System.Windows;
using System.Windows.Controls;

namespace OrandOverlay;

public partial class MainWindow
{
    private readonly NavigationSessionState _navigationSession = new();
    private RouteQuestEvaluation? _routeQuestEvaluation;

    private void ConfirmNavigation_OnClick(object sender, RoutedEventArgs e)
    {
        if (NavigationCombo.SelectedItem is not NavigationOption option) return;
        _navigationSession.Confirm(option.Id);
        RefreshAll($"게임 내 {option.Name} 선택 완료를 사용자 확인으로 기록했습니다.");
    }

    private void ClearNavigation_OnClick(object sender, RoutedEventArgs e)
    {
        _navigationSession.Reset();
        RefreshAll("항법 선택 기록을 지웠습니다. 현재 패에 맞춰 계속 추천합니다.");
    }

    private void RouteQuest_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_updatingSelections) RefreshAll();
    }

    private static string NavigationStatusForDisplay(NativeNavigationSnapshot native, string? confirmed) =>
        native.Status == NativeNavigationStatus.Selected && native.OptionId is { } id
            ? $"게임에서 선택한 항법: {NavigationProfiles.Find(id).Name}"
            : native.Status == NativeNavigationStatus.Unselected ? "게임에서 항법을 아직 선택하지 않았어요."
            : native.Status == NativeNavigationStatus.Conflict ? "게임에서 선택한 항법을 확인하지 못했어요. 확인될 때까지 조합 재료를 사용하지 마세요."
            : confirmed is { } manual
                ? $"직접 선택했다고 알려준 항법: {NavigationProfiles.Find(manual).Name} · 게임에서 선택됐는지는 확인하지 못했어요."
                : "게임에서 선택한 항법을 확인해 주세요.";

    internal void RenderNavigationContext()
    {
        var current = _adaptivePlanningApplied?.InputFingerprint == _pendingAdaptiveFingerprint
            ? _adaptivePlanningApplied?.Navigation : null;
        var candidate = current?.RecommendedOptionId is not null
            ? NavigationSessionState.Candidate(current)
            : _automaticNavigation is { } automatic
                ? $"추천 항법: {automatic.Name} · 게임에서 직접 선택했는지 확인해 주세요."
                : "항법을 고르고 있어요. 게임에서 실제로 선택했는지는 확인하지 못했어요.";
        NavigationStatusText.Text = NavigationStatusForDisplay(_mapSignals.NativeNavigation,
            _navigationSession.ConfirmedOptionId);
        NavigationCandidateText.Text = candidate;
        RouteQuestStatusText.Text = !_liveSessionActive
            ? "게임이 시작되면 항로개척 진행 상황을 확인해요."
            : _mapSignals.RouteQuests.IsVerified ? _mapSignals.RouteQuests.Describe()
            : "항로개척 진행 상황을 확인하지 못했어요. 보상은 아직 계산에 넣지 않아요.";
        ConfirmNavigationButton.Content = NavigationCombo.SelectedItem is NavigationOption option
            ? $"게임에서 {option.Name} 선택 완료" : "게임에서 항법을 선택했어요";
        ConfirmNavigationButton.Visibility = EffectiveNavigation is null &&
            _lastRound is >= 21 and <= 23 ? Visibility.Visible : Visibility.Collapsed;
        var explanation = NavigationComparisonPresentation.Describe(
            current, _routeQuestEvaluation);
        _overlay.RenderNavigationContext(candidate, explanation);
    }
}
