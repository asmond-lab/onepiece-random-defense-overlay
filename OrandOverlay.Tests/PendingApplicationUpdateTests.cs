using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class PendingApplicationUpdateTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DiscoveryNeverInstallsAndNotifiesWhileGameIsActive(bool live)
    {
        var directory = Path.Combine(Path.GetTempPath(), "randypick-pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = OverlayExecutionContext.Production(directory);
            Assert.True(execution.EnsureConsent(() => true));
            var settings = new AppSettings(); using var key = new TestSigningKey();
            var fetches = 0; var installs = 0; var notices = 0;
            var service = new UpdateService(_ => { fetches++; return Task.FromResult(key.Sign(TestSigningKey.Payload(version: "1.0.1"))); }, key.Trust, currentIdentity: "1.0.0");
            var coordinator = new AppUpdateCoordinator(execution, settings, () => live, _ => { notices++; return Task.CompletedTask; },
                (_, _) => { installs++; return Task.CompletedTask; }, serviceFactory: () => service, canSelfInstall: () => true);
            await coordinator.CheckForUpdateAsync();
            Assert.Equal(1, fetches); Assert.Equal(1, notices); Assert.Equal(0, installs);
            Assert.Equal("", settings.LastAttemptedUpdateTag);
            Assert.NotNull(coordinator.Pending);
            if (live) { await coordinator.InstallPendingAsync(); Assert.Equal(0, installs); }
            live = false; await coordinator.CheckForUpdateAsync();
            Assert.Equal(0, installs);
            await coordinator.InstallPendingAsync(); Assert.Equal(1, installs);
            coordinator.Stop(); await coordinator.InstallPendingAsync(); Assert.Equal(1, installs);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
