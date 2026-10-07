namespace OrandOverlay;

/// <summary>Discovers verified offers automatically; installation requires an explicit request.</summary>
internal sealed class AppUpdateCoordinator(
    OverlayExecutionContext execution,
    AppSettings settings,
    Func<bool> isLiveSession,
    Func<string, Task> notifyFooter,
    Func<UpdateService, UpdateInfo, Task> install,
    Func<bool>? autoUpdatesEnabled = null,
    Func<UpdateService?>? serviceFactory = null,
    Func<bool>? canSelfInstall = null)
{
    private int _busy, _checking, _stopped, _installRequested;
    private string? _noticeTag;
    private UpdateService? _pendingService;
    private UpdateInfo? _pending;
    public bool IsBusy => System.Threading.Volatile.Read(ref _busy) != 0;
    public bool BeginInstall() => System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    public void EndInstall() => System.Threading.Interlocked.Exchange(ref _busy, 0);
    public void Stop() { System.Threading.Interlocked.Exchange(ref _stopped, 1); _pending = null; _pendingService = null; }
    public bool AutoUpdatesEnabled => autoUpdatesEnabled?.Invoke() ?? settings.AutoUpdateEnabled;
    private bool RuntimeAllowed => System.Threading.Volatile.Read(ref _stopped) == 0 && execution.RuntimeEnabled && execution.HasCurrentConsent;
    public UpdateInfo? Pending => RuntimeAllowed ? _pending : null;
    public bool CanInstallPending => Pending is not null && _pendingService is { } service && !IsBusy && !isLiveSession() &&
        (canSelfInstall?.Invoke() ?? service.ServiceCanSelfInstall);

    public Task RunStartupAsync() => CheckForUpdateAsync();

    public async Task CheckForUpdateAsync(bool manual = false)
    {
        bool Allowed() => RuntimeAllowed && (manual || AutoUpdatesEnabled);
        if (!Allowed() || IsBusy || System.Threading.Interlocked.CompareExchange(ref _checking, 1, 0) != 0) return;
        try
        {
            var service = serviceFactory is null ? execution.CreateUpdateService() : serviceFactory();
            if (service is null) return;
            var (update, failed) = await service.CheckDetailedAsync();
            if (!Allowed() || IsBusy) return;
            if (failed)
            {
                if (manual) await notifyFooter("새 버전을 확인하지 못했어요. 인터넷 연결을 확인하고 잠시 후 다시 눌러 주세요.");
                return;
            }
            if (update is null)
            {
                _pending = null; _pendingService = null; _noticeTag = null;
                if (manual) await notifyFooter("이미 최신 버전을 사용하고 있어요.");
                return;
            }
            if (!service.ReverifyOffer(update)) return;
            _pendingService = service; _pending = update;
            if (manual || !update.Tag.Equals(_noticeTag, StringComparison.Ordinal))
            {
                _noticeTag = update.Tag;
                await notifyFooter("새 버전이 있어요. 게임을 종료한 뒤 새 버전 알림을 눌러 설치해 주세요.");
            }
        }
        finally { System.Threading.Interlocked.Exchange(ref _checking, 0); }
    }

    public async Task InstallPendingAsync()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installRequested, 1, 0) != 0) return;
        try
        {
            if (!CanInstallPending || _pendingService is not { } service || _pending is not { } update) return;
            if (!service.ReverifyOffer(update)) return;
            service.AutomaticCommitAllowed = () => RuntimeAllowed && !isLiveSession() &&
                (canSelfInstall?.Invoke() ?? service.ServiceCanSelfInstall);
            if (!service.AutomaticCommitAllowed()) return;
            await install(service, update);
        }
        finally { System.Threading.Interlocked.Exchange(ref _installRequested, 0); }
    }
}
