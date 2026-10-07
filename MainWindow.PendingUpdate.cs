using System.Windows;

namespace OrandOverlay;

public partial class MainWindow
{
    private bool _pendingUpdatePromptOpen;
    private bool HasPendingApplicationUpdate => !_updateLifecycleClosed && _updateCoordinator?.Pending is not null;

    private void InitializePendingUpdateNotices()
    {
        MainPendingUpdate.Requested += PendingUpdateRequested;
        _overlay.PendingUpdateRequested += PendingUpdateRequested;
    }

    private void RefreshPendingUpdateNotice()
    {
        if (_updateLifecycleClosed) return;
        var coordinator = _updateCoordinator;
        var ready = !ApplicationUpdateSafety.ShouldDefer(_updateUiReady, _updateLifecycleClosed, _liveSessionActive,
            false, _execution.LiveMemoryEnabled, _updateObservationPending, _updateIdleObservedUtcTicks, DateTime.UtcNow);
        MainPendingUpdate.Present(coordinator?.Pending?.Tag, ready, coordinator?.IsBusy == true);
        _overlay.PresentPendingUpdate(coordinator?.Pending?.Tag, ready, coordinator?.IsBusy == true);
        if (IsLoaded && UsesMap2320) ApplyDiagnosticOverlayVisibility();
    }

    private async void PendingUpdateRequested()
    {
        if (_pendingUpdatePromptOpen || _updateLifecycleClosed || _updateCoordinator is not { Pending: { } update } coordinator) return;
        if (!coordinator.CanInstallPending)
        {
            var message = IsApplicationUpdateSessionActive()
                ? "게임을 종료한 뒤 다시 눌러 주세요. 안전하게 종료된 것이 확인되면 설치할 수 있어요."
                : "이 설치 방식에서는 앱에서 바로 설치할 수 없어요. 새 배포 파일을 받아 직접 교체해 주세요.";
            FooterStatus.Text = message; _overlay.UpdateStatus(message); return;
        }
        _pendingUpdatePromptOpen = true;
        try
        {
            if (MessageBox.Show(this, RandyPickBrand.VersionLabel(update.Latest).Replace("BETA ", "베타 ", StringComparison.Ordinal) + " 버전을 설치할까요?\n설치하면 랜디픽을 다시 시작합니다.",
                    "새 버전 설치", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            await coordinator.InstallPendingAsync();
        }
        finally { _pendingUpdatePromptOpen = false; RefreshPendingUpdateNotice(); }
    }

    internal void CapturePendingUpdateNotice(string? tag, bool ready, bool busy = false)
    {
        if (_runtimeEffects) throw new InvalidOperationException("Preview state is fixture-only.");
        MainPendingUpdate.Present(tag, ready, busy); _overlay.PresentPendingUpdate(tag, ready, busy);
    }
}
