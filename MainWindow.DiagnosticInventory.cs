using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace OrandOverlay;

public partial class MainWindow
{
    private IDiagnosticInventoryReference? _diagnosticInventory;
    private DiagnosticInventoryPresentationState _diagnosticReferences = new();
    private string _diagnosticRenderKey = "";
    private string _diagnosticCandidateKey = "";
    private NormalCandidateBrowser? _diagnosticCandidates;
    private DispatcherTimer? _diagnosticExpiry;
    private string _diagnosticContext = "";
    private int _diagnosticView = -1;
    private long _diagnosticRevision;
    private long _diagnosticScanGeneration;
    private long _diagnosticMatchGeneration;
    private object? _diagnosticRecognizer;
    private bool _diagnosticClosed;
    private bool _diagnosticLaneActive;
    private bool _diagnosticOverlaySession;
    private OverlayDisplayMode? _diagnosticDisplayMode;
    private string _diagnosticStatus = "유닛 확인 중";

    internal static string DiagnosticRoundDisplay(IDiagnosticInventoryReference? value) =>
        value?.ObservedRound is { } round ? $"{round}라운드" : "라운드 확인 중";

    private NormalCandidateBrowser CreateDiagnosticCandidateBrowser()
    {
        var model = NormalCandidateBrowser.Create(_catalog);
        var craftPlanner = new NormalCraftPlanner(_catalog);
        NormalBrowserView.SetCraftPlanner(craftPlanner);
        _overlay.NormalView.SetCraftPlanner(craftPlanner);
        model.ReferencePresentationIsValid = IsDiagnosticInventoryPresentable;
        model.PresentationChanged += RenderDiagnosticInventoryReference;
        return model;
    }
    private bool IsDiagnosticInventoryPresentable()
    {
        Dispatcher.VerifyAccess();
        return DiagnosticPresentationFencesHold() && _diagnosticInventory is not null &&
            ReferenceEquals(_diagnosticInventory, _diagnosticReferences.Current(DateTimeOffset.UtcNow,
                _catalog.MapVersion, _catalog.SelectedDatasetFingerprint));
    }
    private bool DiagnosticPresentationFencesHold() => !_diagnosticClosed && UsesMap2320 && !_coachPaused &&
        AutoScanCheck.IsChecked == true && _diagnosticScanGeneration == _scanGeneration &&
        _diagnosticMatchGeneration == _adaptivePlanning.MatchGeneration && ReferenceEquals(_diagnosticRecognizer, _recognizer);
    private DispatcherTimer CreateDiagnosticExpiryTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Send, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += DiagnosticExpiryTick;
        return timer;
    }
    private void DiagnosticExpiryTick(object? sender, EventArgs e)
    {
        var current = DiagnosticPresentationFencesHold() ? _diagnosticReferences.Current(DateTimeOffset.UtcNow,
            _catalog.MapVersion, _catalog.SelectedDatasetFingerprint) : null;
        if (!ReferenceEquals(current, _diagnosticInventory)) RenderDiagnosticInventoryReference();
    }

    private bool IsDiagnosticOverlaySessionAvailable() => _diagnosticOverlaySession && DiagnosticPresentationFencesHold();

    // Uses only the reference fence and the user's existing display preferences. Never changes those preferences.
    private void ApplyDiagnosticOverlayVisibility()
    {
        Dispatcher.VerifyAccess();
        if (!UsesMap2320) return;
        if (_diagnosticInventory is not null && !IsDiagnosticInventoryPresentable())
        {
            RenderDiagnosticInventoryReference(); // Clears values, then re-enters with a null reference.
            return;
        }
        var sessionVisible = IsDiagnosticOverlaySessionAvailable();
        var visibility = DiagnosticReferencePresentationPolicy.Visibility(sessionVisible, CurrentPlayMode,
            _settings.OverlayDisplayMode, _settings.LastVisibleOverlayDisplayMode);
        var noticeVisible = HasPendingApplicationUpdate && _settings.OverlayDisplayMode == OverlayDisplayMode.Full;
        visibility = visibility with { RecommendationVisible = visibility.RecommendationVisible || noticeVisible };
        _craftWindow?.SetDisplayAllowed(sessionVisible && _settings.OverlayDisplayMode != OverlayDisplayMode.Hidden);
        var visibilityChanged = _overlay.IsVisible != visibility.RecommendationVisible || _overlay.Stats.IsVisible != visibility.StatsVisible;
        var modeChanged = _diagnosticDisplayMode != _settings.OverlayDisplayMode;
        if (modeChanged)
        {
            _overlay.Stats.SetDisplayMode(_settings.OverlayDisplayMode);
            _diagnosticDisplayMode = _settings.OverlayDisplayMode;
        }
        if (visibility.RecommendationVisible) { if (!_overlay.IsVisible) _overlay.Show(); }
        else if (_overlay.IsVisible) _overlay.Hide();
        if (visibility.StatsVisible) { if (!_overlay.Stats.IsVisible) _overlay.Stats.Show(); }
        else if (_overlay.Stats.IsVisible) _overlay.Stats.Hide();
        if ((visibility.RecommendationVisible || visibility.StatsVisible) && (visibilityChanged || modeChanged))
        {
            ApplyDefaultOverlayLayout();
            if (visibility.RecommendationVisible) _overlay.EnsureVisible();
            if (visibility.StatsVisible) _overlay.Stats.EnsureVisible();
            _overlay.SetClickThrough(_settings.ClickThroughOverlay);
        }
        OverlayButton.Content = !sessionVisible && !noticeVisible ? "유닛 확인 중" :
            _settings.OverlayDisplayMode == OverlayDisplayMode.Hidden ? "오버레이 보이기" : "오버레이 숨기기";
    }

    // Capture seam: dispatcher-only, read-only domain fields; no reader/process/activation path.
    internal DiagnosticInventoryUiProof CaptureDiagnosticInventoryUiProof()
    {
        Dispatcher.VerifyAccess();
        RenderDiagnosticInventoryReference(); // Revalidates presentation before reporting visible values.
        return new(IsDiagnosticInventoryPresentable(), _diagnosticInventory?.Entries.Sum(x => x.Count),
            _overlay.IsVisible, _overlay.Stats.IsVisible, _overlay.NormalView.IsVisible,
            RecognitionStatus.Text, _overlay.Stats.DiagnosticReferenceDisplayText,
            _automatic.Count, _coachCurrent, _liveSessionActive, _outcome.Outcome);
    }

    // Stop, pause, failed read, map/source change and Reset hooks. Keep high-water revision: no replay after invalidation.
    private void InvalidateDiagnosticInventoryObservation(bool resetContext = false)
    {
        Dispatcher.VerifyAccess();
        if (!UsesMap2320 && !_diagnosticLaneActive && _diagnosticInventory is null) return;
        _activityCapture?.Gap(resetContext ? "context-reset" : _coachPaused ? "paused" :
            AutoScanCheck.IsChecked != true ? "auto-off" : "presentation-invalidated",
            _adaptivePlanning.MatchGeneration);
        _gameActivityCapture?.Reset();
        if (_diagnosticInventory is not null)
            _diagnosticStatus = _coachPaused ? "인식 일시정지" : AutoScanCheck.IsChecked != true
                ? "자동 인식 꺼짐" : resetContext ? "새 게임의 유닛 확인 중" : DiagnosticPresentationLossStatus();
        if (!resetContext && !_coachPaused && AutoScanCheck.IsChecked == true && _diagnosticInventory is { } lost)
        {
            var expired = DateTimeOffset.UtcNow - lost.StartedAt >= DiagnosticInventoryObservation.FreshnessBudget;
            _observedCapture?.PresentationState(expired ? "expired" : "rejected", expired ? "freshness" : "binding", DateTimeOffset.UtcNow);
        }
        if (resetContext) _observedCapture?.Reset();
        if (_coachPaused) _observedCapture?.PresentationState("paused", "none", DateTimeOffset.UtcNow);
        else if (AutoScanCheck.IsChecked != true) _observedCapture?.PresentationState("auto-off", "none", DateTimeOffset.UtcNow);
        _diagnosticReferences.Invalidate(resetContext);
        _diagnosticInventory = null; _diagnosticExpiry?.Stop();
        if (resetContext || _coachPaused || AutoScanCheck.IsChecked != true ||
            _diagnosticScanGeneration != _scanGeneration || _diagnosticMatchGeneration != _adaptivePlanning.MatchGeneration ||
            !ReferenceEquals(_diagnosticRecognizer, _recognizer))
            _diagnosticOverlaySession = false;
        if (resetContext)
        {
            _diagnosticContext = ""; _diagnosticView = -1; _diagnosticCandidates?.Reset();
        }
        _diagnosticCandidates?.InvalidateReference(_coachPaused);
        if (resetContext || _coachPaused || AutoScanCheck.IsChecked != true)
            _overlay?.ClearDiagnosticStats();
        if (UsesMap2320) RenderDiagnosticInventoryReference();
        else
        {
            _overlay?.Stats.ClearDiagnosticReference();
            if (_normalCandidates is not null) { NormalBrowserView.SetModel(_normalCandidates); _overlay?.NormalView.SetModel(_normalCandidates); }
        }
    }
    private void CloseDiagnosticInventoryObservation()
    {
        Dispatcher.VerifyAccess();
        InvalidateDiagnosticInventoryObservation(true); _diagnosticClosed = true;
        if (_diagnosticExpiry is not null) { _diagnosticExpiry.Stop(); _diagnosticExpiry.Tick -= DiagnosticExpiryTick; _diagnosticExpiry = null; }
        if (_diagnosticCandidates is not null) _diagnosticCandidates.PresentationChanged -= RenderDiagnosticInventoryReference;
        _overlay?.Stats.ClearDiagnosticReference();
    }
}

public sealed record DiagnosticInventoryUiProof(bool ReferenceCurrent, int? ObservedCount,
    bool RecommendationVisible, bool StatsVisible, bool NormalBrowserVisible, string RecognitionStatus,
    string ReferenceStatsText, int AutomaticCount, bool CoachCurrent, bool LiveSessionActive, string? Outcome);

public static class DiagnosticReferencePresentationPolicy
{
    public static bool ToggleAvailable(bool session, bool fences, bool pendingUpdate) => session && fences || pendingUpdate;
    public static OverlayWindowVisibility Visibility(bool current, PlayMode playMode,
        OverlayDisplayMode mode, OverlayDisplayMode lastMode)
    {
        var requested = OverlayDisplayPolicy.Visibility(new(mode, lastMode, current));
        return requested with { RecommendationVisible = requested.RecommendationVisible && playMode == PlayMode.Normal };
    }
    public static string RejectionStatus(RecognitionResult result, string reason)
    {
        if (result.State == RecognitionState.Unsupported) return "지원하지 않는 워크 버전입니다. 업데이트를 확인해 주세요.";
        if (result.State == RecognitionState.UnverifiedProfile) return "이 워크 버전의 인식 기능은 준비 중입니다.";
        if (result.State == RecognitionState.ConfigurationError) return "인식 정보를 불러오지 못했어요. 앱을 다시 실행해 주세요.";
        if (result.State == RecognitionState.Waiting) return result.ConfirmsSessionBoundary
            ? "게임 입장 대기 중" : "게임 정보를 확인 중";
        if (result.State == RecognitionState.TransientReadError) return "유닛 읽기 실패 · 다시 확인 중";
        var producerReason = result.DiagnosticObservation?.Reason ?? "";
        if (reason.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
            producerReason.Contains("freshness", StringComparison.OrdinalIgnoreCase) || producerReason.Contains("budget", StringComparison.OrdinalIgnoreCase))
            return "유닛 정보가 오래되어 다시 확인 중";
        if (reason.Contains("provenance", StringComparison.OrdinalIgnoreCase))
            return "게임 상태가 바뀌어 다시 확인 중";
        return "유닛 정보를 확인 중";
    }
}

// Presentation-only helper lives in this scoped file: no CoachFrame, RenderCoach or gameplay readiness.
public partial class OverlayWindow
{
    internal void SetNormalBrowserActive(bool normal, PlayMode mode, string status)
    {
        Dispatcher.VerifyAccess();
        SetModePresentation(mode);
        OverlayModeBar.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
        NormalBrowserView.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
        CoachView.Visibility = Visibility.Collapsed;
        NowWell.Visibility = FlowWell.Visibility = BoardWell.Visibility = ExpertToolbar.Visibility = Visibility.Collapsed;
        PhaseHintText.Visibility = CarryModeText.Visibility = ReadinessText.Visibility = NavigationCandidateText.Visibility = Visibility.Collapsed;
        GoalText.Text = "랜디픽 · 유닛 확인";
        StatusText.Text = status;
        StatusText.Foreground = RandyPickTheme.Warning;
    }
}
