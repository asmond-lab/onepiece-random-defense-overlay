using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AutomaticUpdateCoordinatorTests
{
    [Theory]
    [InlineData("optout")] [InlineData("match")] [InlineData("stop")] [InlineData("consent")]
    public async Task AwaitedCheckCannotCommitAfterPolicyChanges(string change)
    {
        var directory = Path.Combine(Path.GetTempPath(), "orand-update-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var execution = OverlayExecutionContext.Production(directory);
            Assert.True(execution.EnsureConsent(() => true));
            var settings = new AppSettings(); var live = false; var installs = 0;
            using var key = new TestSigningKey();
            var pending = new TaskCompletionSource<string>();
            var service = new UpdateService(_ => pending.Task, key.Trust, currentIdentity: "1.0.0-test.1");
            var coordinator = new AppUpdateCoordinator(execution, settings, () => live, _ => Task.CompletedTask,
                (_, _) => { installs++; return Task.CompletedTask; }, () => settings.AutoUpdateEnabled, () => service, () => true);
            var check = coordinator.CheckForUpdateAsync();
            if (change == "optout") settings.AutoUpdateEnabled = false;
            if (change == "match") live = true;
            if (change == "stop") coordinator.Stop();
            if (change == "consent") File.Delete(Path.Combine(directory, "settings.json"));
            var payload = TestSigningKey.Payload(version: "1.0.0-test.2"); payload["channel"] = "test";
            pending.SetResult(key.Sign(payload)); await check;
            Assert.Equal(0, installs); Assert.Equal("", settings.LastAttemptedUpdateTag);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task FixturesAndOptOutCannotEvenCreateService()
    {
        var calls = 0; var settings = new AppSettings();
        var fixture = new AppUpdateCoordinator(OverlayExecutionContext.Fixture(settings), settings, () => false,
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, serviceFactory: () => { calls++; return null; });
        await fixture.RunStartupAsync(); await fixture.CheckForUpdateAsync(); Assert.Equal(0, calls);
        var directory = Path.Combine(Path.GetTempPath(), "orand-update-optout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = OverlayExecutionContext.Production(directory); Assert.True(execution.EnsureConsent(() => true));
            settings.AutoUpdateEnabled = false;
            var optedOut = new AppUpdateCoordinator(execution, settings, () => false, _ => Task.CompletedTask,
                (_, _) => Task.CompletedTask, serviceFactory: () => { calls++; return null; });
            await optedOut.CheckForUpdateAsync(); Assert.Equal(0, calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ChecksAreExclusiveAndFailedTagsAreNotRetried()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orand-update-exclusive-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = OverlayExecutionContext.Production(directory); Assert.True(execution.EnsureConsent(() => true));
            var settings = new AppSettings(); using var key = new TestSigningKey();
            var pending = new TaskCompletionSource<string>(); var fetches = 0; var installs = 0; var notices = 0;
            var service = new UpdateService(_ => { fetches++; return pending.Task; }, key.Trust, currentIdentity: "1.0.0");
            var coordinator = new AppUpdateCoordinator(execution, settings, () => false, _ => { notices++; return Task.CompletedTask; },
                (_, _) => { installs++; return Task.CompletedTask; }, serviceFactory: () => service, canSelfInstall: () => true);
            var first = coordinator.CheckForUpdateAsync(); await coordinator.CheckForUpdateAsync(); Assert.Equal(1, fetches);
            pending.SetResult(key.Sign(TestSigningKey.Payload(version: "1.0.1"))); await first;
            await coordinator.CheckForUpdateAsync(); await coordinator.CheckForUpdateAsync();
            Assert.Equal(0, installs); Assert.Equal(1, notices); Assert.Equal("", settings.LastAttemptedUpdateTag);
            await coordinator.InstallPendingAsync();
            Assert.Equal(1, installs);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CommitGateRemainsLiveDuringInstallCallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orand-update-commit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var execution = OverlayExecutionContext.Production(directory); Assert.True(execution.EnsureConsent(() => true));
            var settings = new AppSettings(); using var key = new TestSigningKey(); var live = false;
            var service = new UpdateService(_ => Task.FromResult(key.Sign(TestSigningKey.Payload(version: "1.0.1"))), key.Trust, currentIdentity: "1.0.0");
            var coordinator = new AppUpdateCoordinator(execution, settings, () => live, _ => Task.CompletedTask,
                async (s, _) => { Assert.True(s.AutomaticCommitAllowed!()); await Task.Yield(); live = true; Assert.False(s.AutomaticCommitAllowed!());
                    live = false; settings.AutoUpdateEnabled = false; Assert.True(s.AutomaticCommitAllowed!()); },
                serviceFactory: () => service, canSelfInstall: () => true);
            await coordinator.CheckForUpdateAsync();
            await coordinator.InstallPendingAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DeveloperFlagNeverOverridesActiveMatch(bool developer) => Assert.False(UpdatePolicy.ShouldInstallNow(true, developer));
}
