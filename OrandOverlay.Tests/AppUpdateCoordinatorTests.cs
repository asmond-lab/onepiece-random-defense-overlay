using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

/// <summary>
/// AppUpdateCoordinator의 설치 배타 구간 계약. 네트워크·UI 없이 순수 상태만 검증.
/// </summary>
public sealed class AppUpdateCoordinatorTests
{
    private static AppUpdateCoordinator Create() => new(
        OverlayExecutionContext.Fixture(new AppSettings()),
        new AppSettings(),
        isLiveSession: () => false,
        notifyFooter: _ => Task.CompletedTask,
        install: (_, _) => Task.CompletedTask);

    [Fact]
    public void BeginInstall_IsExclusive_UntilEndInstall()
    {
        var coordinator = Create();
        Assert.False(coordinator.IsBusy);

        Assert.True(coordinator.BeginInstall());
        Assert.True(coordinator.IsBusy);
        Assert.False(coordinator.BeginInstall(), "설치 중에는 두 번째 설치가 막혀야 한다");

        coordinator.EndInstall();
        Assert.False(coordinator.IsBusy);
        Assert.True(coordinator.BeginInstall(), "실패로 닫힌 뒤에는 다시 시도 가능");
    }
}
