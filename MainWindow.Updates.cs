using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

public partial class MainWindow
{
    private volatile bool _updateLifecycleClosed;
    private volatile bool _updateUiReady;
    private volatile bool _automaticUpdatesEnabled;
    // An unverified/unfinished recognition is not proof that a match is idle.
    private volatile bool _updateObservationPending = true;
    private long _updateIdleObservedUtcTicks;

    private void InitializeApplicationUpdates(bool startRuntime)
    {
        _automaticUpdatesEnabled = _settings.AutoUpdateEnabled;
        if (!startRuntime || !_execution.RuntimeEnabled || !_execution.HasCurrentConsent) return;
        _updateObservationPending = _execution.LiveMemoryEnabled;
        _updateCoordinator = new AppUpdateCoordinator(_execution, _settings,
            IsApplicationUpdateSessionActive,
            message => Dispatcher.InvokeAsync(new Action(() => { FooterStatus.Text = message; RefreshPendingUpdateNotice(); })).Task,
            InstallUpdateAsync, () => _automaticUpdatesEnabled);
        _updateTimer.Tick += async (_, _) => { await _updateCoordinator.CheckForUpdateAsync(); RefreshPendingUpdateNotice(); };
        Loaded += (_, _) =>
        {
            if (_updateUiReady || _updateLifecycleClosed) return;
            _updateUiReady = true;
            _ = _updateCoordinator.RunStartupAsync();
            _updateTimer.Start();
        };
    }

    private bool IsApplicationUpdateSessionActive() => ApplicationUpdateSafety.ShouldDefer(
        _updateUiReady, _updateLifecycleClosed, System.Threading.Volatile.Read(ref _liveSessionActive),
        System.Threading.Volatile.Read(ref _scanInProgress), _execution.LiveMemoryEnabled, _updateObservationPending,
        System.Threading.Volatile.Read(ref _updateIdleObservedUtcTicks), DateTime.UtcNow);

    private void ObserveApplicationUpdateSafety(RecognitionResult result)
    {
        // Only a positive, fenced boundary is idle. Missing data and diagnostic Ready
        // cannot cause an in-game restart. This consumes observations, never reads a process.
        var wasPending = _updateObservationPending;
        if (ApplicationUpdateSafety.IsPositiveIdle(result))
        {
            System.Threading.Volatile.Write(ref _updateIdleObservedUtcTicks, DateTime.UtcNow.Ticks);
            _updateObservationPending = false;
            if ((wasPending || _liveSessionActive) && _updateCoordinator is not null)
                Dispatcher.BeginInvoke(new Action(() => { _ = _updateCoordinator.CheckForUpdateAsync(); }));
        }
        else _updateObservationPending = true;
        RefreshPendingUpdateNotice();
    }

    private void ApplicationUpdate_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _automaticUpdatesEnabled = ApplicationUpdateCheck.IsChecked == true;
        _settings.AutoUpdateEnabled = _automaticUpdatesEnabled;
        var preferenceSaved = TrySaveApplicationUpdateState();
        FooterStatus.Text = _automaticUpdatesEnabled
            ? "새 버전을 자동으로 확인합니다. 설치는 게임을 종료한 뒤 직접 시작해 주세요."
            : "자동 확인을 껐어요. '업데이트 확인' 버튼은 계속 사용할 수 있어요.";
        if (!preferenceSaved) FooterStatus.Text += " 설정을 저장하지 못해 앱을 닫으면 이 선택이 사라집니다.";
        if (_automaticUpdatesEnabled && _updateCoordinator is not null)
            _ = _updateCoordinator.CheckForUpdateAsync();
    }

    private async void CheckUpdateNow_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_runtimeEffects || !_execution.HasCurrentConsent || _updateLifecycleClosed) return;
        if (_updateCoordinator is null || _updateCoordinator.IsBusy) return;
        FooterStatus.Text = "새 버전을 확인하고 있어요…";
        await _updateCoordinator.CheckForUpdateAsync(manual: true);
        RefreshPendingUpdateNotice();
    }

    private bool TrySaveApplicationUpdateState()
    {
        if (!_persistSettings) return true;
        try { _execution.SaveSettings(_settings); return true; }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Update settings could not be saved: {0}", error);
            return false;
        }
    }

    private async Task InstallUpdateAsync(UpdateService service, UpdateInfo update)
    {
        if (!_runtimeEffects || !_execution.HasCurrentConsent || _updateLifecycleClosed) return;
        var coordinator = _updateCoordinator;
        if (coordinator is null || !coordinator.BeginInstall()) return;
        Window? progressWindow = null;
        ProgressBar? progressBar = null;
        TextBlock? progressLabel = null;
        var previousAttemptTag = _settings.LastAttemptedUpdateTag;
        var replacementStarted = false;
        try
        {
            var displayVersion = RandyPickBrand.VersionLabel(update.Latest).Replace("BETA ", "베타 ", StringComparison.Ordinal);
            RefreshPendingUpdateNotice();
            // Everything after acquiring the latch, including dispatcher setup, is protected.
            await Dispatcher.InvokeAsync(() =>
            {
                if (_updateLifecycleClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || !IsLoaded)
                    throw new OperationCanceledException("Update UI is closing.");
                FooterStatus.Text = $"{displayVersion} 다운로드 중이에요. 파일 확인 후 앱을 교체하고 다시 시작합니다.";
                progressLabel = new TextBlock { Text = $"{displayVersion} 다운로드 중이에요…", Foreground = RandyPickTheme.Text,
                    FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };
                progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14, Width = 320,
                    Foreground = RandyPickTheme.Text, Background = RandyPickTheme.Raised,
                    BorderBrush = RandyPickTheme.Border };
                var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
                body.Children.Add(progressLabel); body.Children.Add(progressBar);
                progressWindow = new Window { Title = "랜디픽 업데이트", Owner = this,
                    SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = RandyPickTheme.Canvas, Topmost = true,
                    ResizeMode = ResizeMode.NoResize, Content = body };
                progressWindow.Show();
            });
            var progress = new Progress<double>(value =>
            {
                if (_updateLifecycleClosed || progressBar is null || progressLabel is null) return;
                progressBar.Value = value * 100;
                progressLabel.Text = $"{displayVersion} 다운로드 중이에요… {value:P0}";
            });
            _settings.LastAttemptedUpdateTag = update.Tag;
            if (!TrySaveApplicationUpdateState()) throw new IOException("Cannot persist the update attempt.");
            await service.DownloadAndInstallAsync(update, progress);
            replacementStarted = true;
            if (!_updateLifecycleClosed && !Dispatcher.HasShutdownStarted)
                await Dispatcher.InvokeAsync(() =>
                {
                    if (_updateLifecycleClosed || Dispatcher.HasShutdownStarted) return;
                    if (progressLabel is not null) progressLabel.Text = $"{displayVersion} 파일 확인 완료. 앱을 다시 시작해요…";
                    Application.Current.Shutdown();
                });
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("Update installation failed: {0}", error);
            var displayVersion = RandyPickBrand.VersionLabel(update.Latest).Replace("BETA ", "베타 ", StringComparison.Ordinal);
            var deferred = service.InstallDeferred || (!replacementStarted && _updateLifecycleClosed);
            if (deferred) _settings.LastAttemptedUpdateTag = previousAttemptTag;
            TrySaveApplicationUpdateState();
            if (!_updateLifecycleClosed && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                FooterStatus.Text = deferred
                    ? $"{displayVersion} 설치를 미뤘어요. 설정이나 게임 상태가 바뀌었거나 앱을 닫는 중일 수 있어요. 게임을 종료한 뒤 다시 확인해 주세요."
                    : $"{displayVersion} 설치하지 못했어요. 새 버전 알림을 눌러 다시 시도해 주세요.";
        }
        finally
        {
            // Persistence or a cancelled dispatcher operation must never strand the latch.
            coordinator.EndInstall();
            RefreshPendingUpdateNotice();
            if (progressWindow is not null && !_updateLifecycleClosed && !Dispatcher.HasShutdownStarted)
                try { progressWindow.Close(); } catch { }
        }
    }
}
