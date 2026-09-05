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
        RefreshAll("항법 미선택으로 정정했습니다. 적응형 계산은 계속됩니다.");
    }

    private void RouteQuest_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_updatingSelections) RefreshAll();
    }

    internal void RenderNavigationContext()
    {
        var current = _adaptivePlanningApplied?.InputFingerprint == _pendingAdaptiveFingerprint
            ? _adaptivePlanningApplied?.Navigation : null;
        var candidate = NavigationSessionState.Candidate(current);
        NavigationStatusText.Text = _navigationSession.ConfirmedOptionId is { } confirmed
            ? $"게임 내 선택: {NavigationProfiles.Find(confirmed).Name} (사용자 확인)" :
                "게임 내 항법 미선택 · 자동 인식 미지원";
        NavigationCandidateText.Text = candidate;
        RouteQuestStatusText.Text = _mapSignals.RouteQuests.Describe();
        ConfirmNavigationButton.Content = NavigationCombo.SelectedItem is NavigationOption option
            ? $"게임에서 {option.Name} 선택 완료" : "게임 내 선택 완료 확인";
        var explanation = NavigationComparisonPresentation.Describe(
            current, _routeQuestEvaluation);
        _overlay.RenderNavigationContext(candidate, explanation);
    }
}
