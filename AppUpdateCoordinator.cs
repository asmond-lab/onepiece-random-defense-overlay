namespace OrandOverlay;

/// <summary>
/// 자동 업데이트 확인 정책·중복 알림 억제·설치 중복 방지를 담당하는 코디네이터.
/// MainWindow에서 동작 보존으로 추출했다. 실제 다운로드·진행바 UI는 호출부
/// 콜백(install)과 알림 콜백(notifyFooter)에 위임한다 — 로직 변경 금지.
/// </summary>
internal sealed class AppUpdateCoordinator(
    AppSettings settings,
    Func<bool> isLiveSession,
    Func<string, Task> notifyFooter,
    Func<UpdateService, UpdateInfo, Task> install)
{
    private bool _busy;
    private string? _noticeTag;

    /// <summary>설치 진행 중 여부 — 수동 확인 버튼도 이 게이트를 공유한다.</summary>
    public bool IsBusy => _busy;

    /// <summary>설치 배타 구간 시작. false면 이미 설치가 진행 중.</summary>
    public bool BeginInstall()
    {
        if (_busy) return false;
        _busy = true;
        return true;
    }

    /// <summary>설치 실패로 되돌아왔을 때 배타 구간을 닫는다(성공 경로는 재시작으로 끝난다).</summary>
    public void EndInstall() => _busy = false;

    /// <summary>시작 시 1회: 실패한 업데이트의 잔여 .new 파일을 정리하고 확인을 돌린다.</summary>
    public async Task RunStartupAsync()
    {
        try
        {
            if (Environment.ProcessPath is { Length: > 0 } processPath &&
                File.Exists(processPath + ".new"))
                File.Delete(processPath + ".new");
        }
        catch
        {
            // 잔여 파일 정리 실패는 업데이트 확인을 막지 않는다.
        }
        await CheckForUpdateAsync();
    }

    // 새 릴리스가 있으면 확인 없이 내려받아 교체하고 자동 재시작한다(유저 지시).
    // 다만 교체는 재시작을 동반하므로 판 도중에는 미룬다(유저 지시) — 다음 확인
    // 주기에 다시 시도한다. 개발 PC(ORAND_DEV)는 검증을 위해 즉시 교체한다.
    // 같은 태그를 이미 시도했다면(버전 미상승 등) 반복하지 않는다.
    public async Task CheckForUpdateAsync()
    {
        if (_busy) return;
        if (UpdateService.IsTestBuild) return;
        if (!UpdatePolicy.ShouldInstallNow(isLiveSession(), UpdatePolicy.IsDeveloperMachine)) return;
        var service = new UpdateService();
        var update = await service.CheckAsync();
        if (update is null) return;
        if (update.Tag.Equals(settings.LastAttemptedUpdateTag, StringComparison.OrdinalIgnoreCase))
        {
            await NotifyUpdateOnceAsync(update.Tag,
                $"{update.Tag} 자동 업데이트가 이전에 완료되지 않았습니다 — 릴리스 페이지에서 수동으로 받아주세요.");
            return;
        }
        if (!UpdateService.CanSelfInstall)
        {
            await NotifyUpdateOnceAsync(update.Tag,
                $"새 버전 {update.Tag} 공개 — 단일 exe 배포가 아니어서 자동 교체를 건너뜁니다.");
            return;
        }
        await install(service, update);
    }

    // 주기 확인이 같은 안내를 footer에 반복해서 쓰지 않게 태그당 1회만 알린다.
    private async Task NotifyUpdateOnceAsync(string tag, string message)
    {
        if (tag.Equals(_noticeTag, StringComparison.OrdinalIgnoreCase)) return;
        _noticeTag = tag;
        await notifyFooter(message);
    }
}
