using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;

namespace OrandOverlay;

public partial class OverlayWindow
{
    private bool _coachEnabled;
    private bool _detachedCraftLayout;
    private PlayMode _presentedMode;
    internal void EnableDetachedCraftLayout() => _detachedCraftLayout = true;
    public event Action<PlayMode>? ModeRequested;
    private bool _overlayModesReady;
    public void SetModePresentation(PlayMode selected)
    {
        var heightBefore = DesignHeight;
        _presentedMode = selected;
        if (IsLoaded && heightBefore != DesignHeight) ApplyResolutionScale();
        if (!_overlayModesReady)
        {
            _overlayModesReady = true;
            foreach (var option in PlayModes.Options)
            {
                var mode = option.Mode;
                var button = new Button { Content = option.BetaName, IsEnabled = option.IsBetaAvailable, ToolTip = option.IsBetaAvailable ? "이 모드로 안내를 봅니다." : BetaPlayModes.LockedReason, Tag = mode, Margin = new Thickness(0), Padding = new Thickness(0,7,0,7), FontSize = 12, BorderThickness = new Thickness(0) };
                AutomationProperties.SetAutomationId(button, "overlay-mode-" + mode.ToString().ToLowerInvariant());
                button.Click += (_, _) => { if (BetaPlayModes.IsAvailable(mode)) ModeRequested?.Invoke(mode); };
                OverlayModeTabs.Children.Add(button);
            }
        }
        foreach (var button in OverlayModeTabs.Children.OfType<Button>())
        {
            var active = button.Tag is PlayMode mode && mode == selected;
            if (active) button.WithOverlayBackground(OverlayChrome.SelectionKey);
            else button.Background = Brushes.Transparent;
            button.Foreground = active ? RandyPickTheme.Text : RandyPickTheme.Muted;
            AutomationProperties.SetItemStatus(button, !button.IsEnabled ? "아직 사용할 수 없음" : active ? "선택됨" : "선택 가능");
        }
    }

    public BeginnerCoachView BeginnerView => CoachView;
    public NormalCandidateView NormalView => NormalBrowserView;

    public void RenderCoach(bool enabled, CoachDecision decision, CoachFrame frame, UnitDefinition? unit)
    {
        Stats.SetObservation(decision, frame);
        _coachEnabled = enabled;
        OverlayModeBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SetModePresentation(frame.Mode);
        CoachView.Visibility = enabled && frame.Mode != PlayMode.Normal ? Visibility.Visible : Visibility.Collapsed;
        NormalBrowserView.Visibility = enabled && frame.Mode == PlayMode.Normal ? Visibility.Visible : Visibility.Collapsed;
        NowWell.Visibility = FlowWell.Visibility = BoardWell.Visibility = ExpertToolbar.Visibility =
            enabled ? Visibility.Collapsed : Visibility.Visible;
        if (enabled)
        {
            GoalText.Text = "랜디픽";
            HideExpertCoachHeader();
            CoachView.Render(decision, frame, unit);
        }
    }

    private void HideExpertCoachHeader()
    {
        if (!_coachEnabled) return;
        PhaseHintText.Visibility = CarryModeText.Visibility = ReadinessText.Visibility =
            NavigationCandidateText.Visibility = Visibility.Collapsed;
    }
}
