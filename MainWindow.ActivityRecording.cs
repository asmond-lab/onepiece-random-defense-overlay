using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace OrandOverlay;

public partial class MainWindow
{
    private LocalActivityLog? _activityLog;
    private LocalActivityCapture? _activityCapture;
    private LocalGameActivityCapture? _gameActivityCapture;
    private TaskCompletionSource? _activityScanFinished;
    private DispatcherTimer? _activityStatusTimer;
    private readonly HashSet<Window> _activityWindows = [];
    private long _activityDropped;
    private Map2321ActivityRules? _activityRules;

    private void InitializeActivityRecording()
    {
        _activityLog = _execution.CreateActivityLog();
        if (_activityLog is null) return;
        _activityStatusTimer = new DispatcherTimer(TimeSpan.FromSeconds(1),
            DispatcherPriority.Background, (_, _) => UpdateLocalActivityRecordingStatus(), Dispatcher);
        _activityStatusTimer.Start();
        _activityCapture = new(_catalog, (kind, data, generation, revision) =>
            _activityLog.TryRecord(kind, data, generation, revision));
        if (_catalog.MapVersion is "2.321" or "2.322" or "2.323")
        {
            try
            {
                _activityRules = Map2321ActivityRules.LoadBundled(_catalog.MapVersion);
                _gameActivityCapture = new(_activityRules, (kind, data, generation, revision) =>
                    _activityLog.TryRecord(kind, data, generation, revision));
                if (_recognizer is WarcraftMemoryRecognitionService reader) reader.ActivityRules = _activityRules;
                RecordActivity("game.capabilities", new
                {
                    SourceSha256 = _catalog.MapVersion == Map2323SourceContract.MapVersion
                        ? Map2323SourceContract.JassSha256 : _catalog.MapVersion == Map2322SourceContract.MapVersion
                        ? Map2322SourceContract.JassSha256 : Map2321ActivityRules.SourceSha256,
                    IntegerArrays = _activityRules.IntegerArrays.Order(StringComparer.Ordinal).ToArray(),
                    RuntimeMapIdentityConfirmed = false, Scope = "diagnostic-current-view",
                    UnboundGambles = _activityRules.Gambles.Where(rule => rule.Attempts is null).Select(rule => rule.Id).ToArray()
                });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                RecordActivity("game.capability-error", new { error.Message });
            }
        }
        RecordActivity("app.start", new
        {
            Version = UpdateService.CurrentBuildVersion, _catalog.MapVersion,
            Synthetic = !_execution.RuntimeEnabled, LocalOnly = true,
            LogDirectory = _execution.ActivityLogDirectory,
            MaxFileBytes = _execution.RuntimeEnabled ? 16L * 1024 * 1024 : (long?)null,
            RetainedFilesPerSession = _execution.RuntimeEnabled ? 32 : (int?)null
        });
        WatchActivityWindow(this);
        WatchActivityWindow(_overlay);
        WatchActivityWindow(_overlay.Stats);
    }

    private void RecordActivity(string kind, object data, long? revision = null)
    {
        _activityLog?.TryRecord(kind, data, _adaptivePlanning.MatchGeneration, revision);
        UpdateLocalActivityRecordingStatus();
    }

    private static string? LocalActivityRecordingWarning(
        bool isBackpressured, long droppedRecords, bool hasError) =>
        hasError ? "로컬 활동 기록 저장 실패 · 디스크와 권한을 확인하세요" :
        droppedRecords > 0 ? $"활동 기록 {droppedRecords}개를 저장하지 못했어요. 저장 공간과 폴더 권한을 확인해 주세요." :
        isBackpressured ? "로컬 활동 기록 저장 지연 · 인식은 계속합니다" : null;

    private bool TryShowLocalActivityRecordingWarning()
    {
        if (_activityLog is not { } log) return false;
        var warning = LocalActivityRecordingWarning(
            log.IsBackpressured, log.DroppedRecords, log.LastError is not null);
        if (warning is null) return false;
        GameplayRecordingStatus.Text = warning;
        GameplayRecordingStatus.Foreground = OverlayTheme.PlanWarning;
        GameplayRecordingStatus.Visibility = Visibility.Visible;
        return true;
    }

    private void UpdateLocalActivityRecordingStatus() =>
        TryShowLocalActivityRecordingWarning();

    private void WatchActivityWindow(Window window)
    {
        if (_activityLog is null || !_activityWindows.Add(window)) return;
        void ControlEvent(object sender, RoutedEventArgs args)
        {
            if (!_initialized) return;
            var control = args.Source as FrameworkElement;
            RecordActivity("ui.control", new
            {
                Window = window.GetType().Name, Event = args.RoutedEvent.Name,
                Control = control?.Name, ControlType = control?.GetType().Name,
                Origin = "routed-control-event", Mode = CurrentPlayMode.ToString(),
                _coachPaused, _settings.AutoScanEnabled, _settings.ClickThroughOverlay,
                OverlayMode = _settings.OverlayDisplayMode.ToString(),
                _settings.GoalUnitId, _settings.GuideNumber,
                SelectedUnitId = _diagnosticCandidates?.SelectedUnitId
            });
        }
        window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ControlEvent), true);
        window.AddHandler(Selector.SelectionChangedEvent,
            new System.Windows.Controls.SelectionChangedEventHandler(ControlEvent), true);
        window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ControlEvent), true);
        window.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(ControlEvent), true);
        window.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, args) =>
            RecordActivity("ui.input", new
            {
                Window = window.GetType().Name, Device = "mouse", Button = args.ChangedButton.ToString(),
                Control = (args.OriginalSource as FrameworkElement)?.Name
            })), true);
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
            RecordActivity("ui.input", new
            {
                Window = window.GetType().Name, Device = "keyboard", Key = args.Key.ToString(),
                Modifiers = Keyboard.Modifiers.ToString(),
                Control = (args.OriginalSource as FrameworkElement)?.Name
            })), true);
        window.IsVisibleChanged += (_, _) => RecordActivity("ui.visibility",
            new { Window = window.GetType().Name, window.IsVisible });
        window.StateChanged += (_, _) => RecordActivity("ui.window-state",
            new { Window = window.GetType().Name, State = window.WindowState.ToString() });
        window.SizeChanged += (_, args) => RecordActivity("ui.window-size",
            new { Window = window.GetType().Name, args.NewSize.Width, args.NewSize.Height });
        window.Closed += (_, _) => _activityWindows.Remove(window);
    }

    private void CaptureActivityRead(string lane, IDiagnosticInventoryReference value,
        RecognitionDiagnostics diagnostics, bool accepted, string reason)
    {
        if (_activityLog is null || _activityCapture is null) return;
        if (_activityDropped != _activityLog.DroppedRecords || _activityLog.LastError is not null)
        {
            _activityDropped = _activityLog.DroppedRecords;
            _activityCapture.Gap("local-recording-loss", _adaptivePlanning.MatchGeneration);
            _gameActivityCapture?.Reset();
        }
        _activityCapture.Observe(lane, value, diagnostics, accepted, reason, _adaptivePlanning.MatchGeneration);
        if (!accepted && lane == "full")
            _gameActivityCapture?.Reset();
        else if (lane == "full")
            _gameActivityCapture?.Observe(value, diagnostics, _adaptivePlanning.MatchGeneration);
    }

    private void CaptureActivityPresentation(long renderingStarted)
    {
        if (_activityLog is null) return;
        var value = _diagnosticInventory;
        RecordActivity("ui.presentation", new
        {
            RenderingMs = Stopwatch.GetElapsedTime(renderingStarted).TotalMilliseconds,
            PresentedAtUtc = DateTimeOffset.UtcNow, value?.StartedAt, value?.CompletedAt,
            value?.Source, value?.ViewSlot, value?.BindingContextId,
            Inventory = value?.Counts, Current = value is not null,
            SelectedUnitId = _diagnosticCandidates?.SelectedUnitId,
            MainRecommendation = NormalBrowserView.DisplayedRecommendationId,
            OverlayRecommendation = _overlay.NormalView.DisplayedRecommendationId,
            RecommendationVisible = _overlay.IsVisible, StatsVisible = _overlay.Stats.IsVisible,
            CraftVisible = _craftWindow?.IsVisible ?? false
        }, value?.SourceRevision);
    }

    private async Task StopActivityRecordingAsync()
    {
        _activityStatusTimer?.Stop();
        if (_activityLog is null) return;
        if (_activityScanFinished is { } scan) await scan.Task;
        RecordActivity("app.stop", new { Reason = "application-close", _activityLog.DroppedRecords });
        await _activityLog.DisposeAsync();
        if (_activityLog.LastError is { } error)
            Trace.TraceError("Local activity recording failed: {0}", error);
    }
}
