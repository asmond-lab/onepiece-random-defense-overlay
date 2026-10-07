using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace OrandOverlay;

public partial class MainWindow
{
    private NormalCandidateBrowser? _normalCandidates;
    private CoachFrame? _normalFrame;
    private void InitializeNormalCandidateBrowser()
    {
        InitializeCraftWindow();
        _normalCandidates = NormalCandidateBrowser.Create(_catalog);
        var craftPlanner = new NormalCraftPlanner(_catalog);
        NormalBrowserView.SetCraftPlanner(craftPlanner);
        _overlay.NormalView.SetCraftPlanner(craftPlanner);
        SizeChanged += (_, _) => UpdateBWorkspaceDensity();
        UpdateBWorkspaceDensity();
        _overlay.ModeRequested += SetPlayMode;
        NormalBrowserView.SetModel(_normalCandidates);
        _overlay.NormalView.SetModel(_normalCandidates);
        _normalCandidates.PresentationChanged += () =>
        {
            if (UsesMap2320) { RenderDiagnosticInventoryReference(); return; }
            // Selection/fold/direction events only render the model's fenced snapshot.
            // Never resurrect an old CoachFrame while recommendation work is awaiting.
            NormalBrowserView.Render(); _overlay.NormalView.Render();
            MainObservationStatus.Text = _normalCandidates?.Snapshot.IsCurrent == true ? "현재 관측 패" : _coachPaused ? "추천 일시정지" : "현재 패 확인 대기";
        };
        foreach (var view in new[] { NormalBrowserView, _overlay.NormalView })
            view.ResumeRequested += ResumeNormalCandidates;
        foreach (var option in PlayModes.Options)
        {
            var button = new Button { Content = option.BetaName, IsEnabled = option.IsBetaAvailable, ToolTip = option.IsBetaAvailable ? "일반 모드 베타" : BetaPlayModes.LockedReason, Tag = option.Mode, Width = 105, Padding = new(0, 6, 0, 6), Margin = new(0),
                Background = System.Windows.Media.Brushes.Transparent, Foreground = RandyPickTheme.Muted,
                BorderBrush = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0) };
            AutomationProperties.SetAutomationId(button, "main-mode-" + option.Mode.ToString().ToLowerInvariant());
            button.Click += (_, _) => SetPlayMode(option.Mode);
            MainModeTabs.Children.Add(button);
        }
    }
    private void UpdateBWorkspaceDensity()
    {
        // Smaller supported windows keep the candidate row usable, without hiding mode/category access.
        var compact = Height < 700;
        BWorkspaceContent.Margin = compact ? new Thickness(25, 14, 25, 10) : new Thickness(25, 22, 25, 16);
        BWorkspaceHeader.Margin = new Thickness(0, 0, 0, compact ? 10 : 14);
        BWorkspaceTitle.FontSize = compact ? 20 : 24;
        BWorkspaceSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BWorkspaceModes.Margin = new Thickness(0, 0, 0, compact ? 10 : 14);
    }
    private void UpdateMainModeTabs()
    {
        _overlay?.SetModePresentation(CurrentPlayMode);
        foreach (var button in MainModeTabs.Children.OfType<Button>())
        {
            var active = button.Tag is PlayMode mode && mode == CurrentPlayMode;
            if (active) button.WithOverlayBackground(OverlayChrome.SelectionKey);
            else button.Background = System.Windows.Media.Brushes.Transparent;
            button.Foreground = active ? RandyPickTheme.Text : RandyPickTheme.Muted;
            AutomationProperties.SetItemStatus(button, !button.IsEnabled ? "locked" : active ? "selected" : "available");
        }
    }
    private void InvalidateNormalCandidateObservation()
    {
        if (UsesMap2320) { RenderDiagnosticInventoryReference(); return; }
        _normalFrame = null;
        MainObservationStatus.Text = _coachPaused ? "추천 일시정지" : "현재 패 확인 대기";
        _normalCandidates?.InvalidateObservation(_coachPaused);
        NormalBrowserView?.Render(); _overlay?.NormalView.Render();
    }
    private void ResumeNormalCandidates()
    {
        if (!_coachPaused) return;
        _coachPaused = false;
        InvalidateNormalCandidateObservation();
        if (UsesMap2320) { InvalidateDiagnosticInventoryObservation(); return; }
        RefreshAll();
    }
    private void RenderNormalCandidateBrowser(CoachFrame frame)
    {
        if (UsesMap2320) { RenderDiagnosticInventoryReference(); return; }
        _normalFrame = frame;
        var current = !UsesMap2320 && _coachCurrent && !_automaticStale && !_automaticDisconnected &&
            !_coachPaused && AutoScanCheck.IsChecked == true && _outcome.Outcome is not ("clear" or "fail") &&
            frame.IsCurrent && !frame.Paused && frame.Outcome is not ("clear" or "fail") &&
            frame.MatchGeneration == _adaptivePlanning.MatchGeneration && frame.RecognitionRevision == _recognitionRevision;
        _normalCandidates?.Update(frame.Inventory, current, _adaptivePlanning.MatchGeneration, _coachPaused);
        MainObservationStatus.Text = current ? "현재 관측 패" : _coachPaused ? "추천 일시정지" : "현재 패 확인 대기";
        NormalBrowserView.Render(); _overlay.NormalView.Render();
    }
}
