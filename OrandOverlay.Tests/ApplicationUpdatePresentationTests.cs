using OrandOverlay;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ApplicationUpdatePresentationTests
{
    private static string Source(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void StartupIsIndependentOfReaderButStillRuntimeConsentAndLoadGated()
    {
        var main = Source("MainWindow.xaml.cs");
        Assert.True(main.IndexOf("InitializeApplicationUpdates(startRuntime);", StringComparison.Ordinal) <
            main.IndexOf("if (!execution.LiveMemoryEnabled) return;", StringComparison.Ordinal));
        var ui = Source("MainWindow.Updates.cs");
        Assert.Contains("!startRuntime || !_execution.RuntimeEnabled || !_execution.HasCurrentConsent", ui);
        Assert.Contains("Loaded +=", ui);
        Assert.Contains("_updateUiReady = true", ui);
        Assert.DoesNotContain("if (UpdateService.IsTestBuild)", ui);
        Assert.Contains("service.AutomaticCommitAllowed", Source("AppUpdateCoordinator.cs"));
        Assert.Contains("VersionText.Text = RandyPickBrand.BetaLabel", main);
        Assert.Contains("_updateCoordinator?.Stop();", main);
        Assert.Contains("ApplicationUpdateCheck", Source("MainWindow.xaml"));
    }

    [Fact]
    public void UnknownOrBusySessionNeverMeansIdle()
    {
        var now = DateTime.UtcNow;
        bool Defer(bool ready = true, bool closing = false, bool live = false, bool scanning = false,
            bool pending = false, long? observed = null) => ApplicationUpdateSafety.ShouldDefer(
                ready, closing, live, scanning, true, pending, observed ?? now.Ticks, now);
        Assert.False(Defer());
        Assert.True(Defer(ready: false));
        Assert.True(Defer(closing: true));
        Assert.True(Defer(live: true));
        Assert.True(Defer(scanning: true));
        Assert.True(Defer(pending: true));
        Assert.True(Defer(observed: 0));
        Assert.True(Defer(observed: now.AddSeconds(1).Ticks));
        Assert.True(Defer(observed: now.AddSeconds(-11).Ticks));
        Assert.True(Defer(observed: long.MaxValue));
        Assert.False(ApplicationUpdateSafety.ShouldDefer(true, false, false, false, false, true, 0, now));
    }

    [Fact]
    public void OnlyPositiveWaitingBoundaryCanUnlockAnIdleUpdate()
    {
        foreach (var state in Enum.GetValues<RecognitionState>())
        {
            Assert.False(ApplicationUpdateSafety.IsPositiveIdle(new RecognitionResult { State = state }));
            Assert.Equal(state == RecognitionState.Waiting, ApplicationUpdateSafety.IsPositiveIdle(
                new RecognitionResult { State = state, ConfirmsSessionBoundary = true }));
        }
    }


    [Fact]
    public void InstallUiSetupAndPersistenceCannotStrandTheInstallLatch()
    {
        // Source only: no MainWindow construction, dispatcher, network or installer execution.
        var code = System.Text.RegularExpressions.Regex.Replace(Source("MainWindow.Updates.cs"),
            "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|@\"(?:\"\"|[^\"])*\"|\"(?:\\\\.|[^\"\\\\])*\"",
            match => new string(' ', match.Length));
        var install = SourceBlock(code, "private async Task InstallUpdateAsync(").Body;
        var protectedBody = SourceBlock(install, "try");
        var tryStart = install.IndexOf("try", StringComparison.Ordinal);
        // Only inert local setup is permitted between acquisition and the protected block.
        Assert.Equal(Compact(@"
            if (!_runtimeEffects || !_execution.HasCurrentConsent || _updateLifecycleClosed) return;
            var coordinator = _updateCoordinator;
            if (coordinator is null || !coordinator.BeginInstall()) return;
            Window? progressWindow = null;
            ProgressBar? progressBar = null;
            TextBlock? progressLabel = null;
            var previousAttemptTag = _settings.LastAttemptedUpdateTag;
            var replacementStarted = false;"), Compact(install[..tryStart]));
        var afterTry = install[protectedBody.End..].TrimStart();
        Assert.StartsWith("catch", afterTry);
        var failure = SourceBlock(afterTry, "catch");
        var afterCatch = afterTry[failure.End..].TrimStart();
        Assert.StartsWith("finally", afterCatch);
        var cleanup = SourceBlock(afterCatch, "finally");
        Assert.Empty(afterCatch[cleanup.End..].Trim());
        // Release is unconditional and first, not conditional on persistence or window cleanup.
        Assert.StartsWith("coordinator.EndInstall();", cleanup.Body.TrimStart());
        Assert.DoesNotContain("Save", cleanup.Body);
        Assert.DoesNotContain("EndInstall", protectedBody.Body);
        Assert.DoesNotContain("EndInstall", failure.Body);
        Assert.Contains("TrySaveApplicationUpdateState();", failure.Body);

        var setup = SourceBlock(protectedBody.Body, "await Dispatcher.InvokeAsync(() =>").Body;
        Assert.StartsWith(Compact(@"
            if (_updateLifecycleClosed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished || !IsLoaded)
                throw new OperationCanceledException("), Compact(setup));
        Assert.Contains("FooterStatus.Text =", setup);
        Assert.Contains("progressLabel = new TextBlock", setup);
        Assert.Contains("progressBar = new ProgressBar", setup);
        Assert.Contains("progressWindow = new Window", setup);
        Assert.Contains("progressWindow.Show();", setup);
        Assert.Contains("var progress = new Progress<double>", protectedBody.Body);
        var save = protectedBody.Body.IndexOf("if (!TrySaveApplicationUpdateState()) throw new IOException(", StringComparison.Ordinal);
        var download = protectedBody.Body.IndexOf("await service.DownloadAndInstallAsync(update, progress);", StringComparison.Ordinal);
        Assert.True(save >= 0 && download > save, "Attempt persistence must fail closed before installation.");
        var persistence = SourceBlock(code, "private bool TrySaveApplicationUpdateState()").Body;
        Assert.Contains("if (!_persistSettings) return true;", persistence);
        Assert.Contains("_execution.SaveSettings(_settings); return true;", persistence);
        Assert.Contains("catch (Exception error)", persistence);
        var persistenceFailure = SourceBlock(persistence, "catch (Exception error)").Body;
        Assert.Contains("Trace.TraceError(", persistenceFailure);
        Assert.Contains("error);", persistenceFailure);
        Assert.Contains("return false;", persistenceFailure);
        Assert.Contains(Compact("if (!_updateLifecycleClosed && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)"),
            Compact(failure.Body));
    }

    private static string Compact(string source) =>
        System.Text.RegularExpressions.Regex.Replace(source, @"\s+", "");

    // Input has comments and string literals masked, so their braces cannot fake block ownership.
    private static (string Body, int End) SourceBlock(string code, string marker)
    {
        var start = code.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing source marker: " + marker);
        var open = code.IndexOf('{', start);
        Assert.True(open >= 0, "Missing source block: " + marker);
        var depth = 1;
        for (var i = open + 1; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            if (code[i] == '}' && --depth == 0) return (code[(open + 1)..i], i + 1);
        }
        throw new InvalidOperationException("Unbalanced source block: " + marker);
    }

    [Fact]
    public void AutoUpdateOptOutPersistsWithoutChangingExistingSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "randypick-update-preference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "settings.json");
        try
        {
            File.WriteAllText(file, "{\"SettingsSchemaVersion\":999,\"AutoScanEnabled\":false,\"GoalUnitId\":\"rawcode:fixture\"}");
            var settings = SettingsStore.Load(file);
            Assert.True(settings.AutoUpdateEnabled);
            settings.AutoUpdateEnabled = false;
            SettingsStore.Save(settings, file);
            var restored = SettingsStore.Load(file);
            Assert.False(restored.AutoUpdateEnabled);
            Assert.False(restored.AutoScanEnabled);
            Assert.Equal("rawcode:fixture", restored.GoalUnitId);
            Assert.Equal("OrandOverlay", Path.GetFileName(AppPaths.UserDataDirectory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
