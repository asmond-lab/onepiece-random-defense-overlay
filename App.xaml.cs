using System.Windows;

namespace OrandOverlay;

public partial class App : System.Windows.Application
{
    private OverlayExecutionContext? _execution;
    internal OverlayExecutionContext Execution
    {
        get => _execution ??= OverlayExecutionContext.Production();
        init => _execution = value ?? throw new ArgumentNullException(nameof(value));
    }
    // Compatibility for existing capture callers; no shared defaults are evaluated.
    internal bool SkipRuntimeStartup
    {
        get => !Execution.RuntimeEnabled;
        init { if (value) _execution = OverlayExecutionContext.Fixture(new AppSettings()); }
    }

    // 중복 실행 방지. 두 인스턴스가 같은 설정 파일과 오버레이 핫키를 두고 다투면
    // 설정이 서로를 덮어쓰고 핫키는 한쪽만 먹는다 — 두 번째 실행은 기존 창을
    // 앞으로 부르고 조용히 끝난다.
    //
    // 자동 업데이트와의 관계: 교체 스크립트는 옛 프로세스가 죽어 exe가 지워질
    // 때까지 기다렸다가 새 버전을 실행한다. 뮤텍스는 프로세스 종료 시 OS가
    // 해제하므로 새 버전이 잠금에 막히는 일은 없다.
    internal string InstanceMutexName { get; init; } = @"Local\OrandOverlay.SingleInstance";
    internal string ActivationEventName { get; init; } = @"Local\OrandOverlay.ShowExisting";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationSignal;
    private Window? _consentWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        // This branch must precede every normal-runtime dependency, including Execution.
        if (RandyPickPackageProbe.TryHandleStartup(e.Args, out var probeExitCode))
        {
            TelemetryConsentStartup.ClearStartupUri(this);
            Shutdown(probeExitCode);
            return;
        }

        // Capture callers may still assign the old URI. Clear it even for inert fixtures.
        TelemetryConsentStartup.ClearStartupUri(this);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!Execution.RuntimeEnabled) return;
        StartRuntime(e);
    }

    private void StartRuntime(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        // IPC precedes consent so a second launch activates the pending dialog too.
        ListenForActivationSignal();
        try
        {
            if (!Execution.EnsureConsent(() =>
            {
                _consentWindow = new TelemetryConsentWindow();
                try { return _consentWindow.ShowDialog(); }
                finally { _consentWindow = null; }
            }))
            {
                Shutdown();
                return;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError("Consent could not be saved: {0}", error);
            MessageBox.Show("동의를 저장하지 못했어요. 저장 공간과 폴더 권한을 확인한 뒤 다시 실행해 주세요. 앱을 종료합니다.",
                "랜디픽", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Execution.RequireConsent();
        base.OnStartup(e);
        MainWindow = new MainWindow(Execution);
        if (MainWindow is MainWindow { StartupAborted: true })
        {
            Shutdown(1);
            return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
        _ = Execution.RefreshProfilesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>기존 인스턴스에 "창 보여줘" 신호를 보낸다. 실패해도 그냥 끝낸다.</summary>
    private void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivationEventName, out var handle))
                using (handle) handle.Set();
        }
        catch (Exception)
        {
            // 신호는 편의 기능이다 — 기존 인스턴스가 살아 있는 것만으로 목적은 달성됐다.
        }
    }

    /// <summary>백그라운드 스레드로 신호를 기다렸다가 메인 창을 앞으로 부른다.</summary>
    private void ListenForActivationSignal()
    {
        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        var thread = new Thread(() =>
        {
            var signal = _activationSignal;
            while (signal is not null)
            {
                try { signal.WaitOne(); }
                catch (ObjectDisposedException) { return; }
                Dispatcher.BeginInvoke(() =>
                {
                    if ((_consentWindow ?? MainWindow) is not { } window) return;
                    window.Show();
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    window.Activate();
                });
            }
        })
        { IsBackground = true, Name = "OrandOverlay.ActivationListener" };
        thread.Start();
    }
}
