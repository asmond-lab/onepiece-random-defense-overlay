using System.Windows;
using System.Windows.Automation;
using Xunit;

namespace OrandOverlay.Tests;

[CollectionDefinition("StartupFailure", DisableParallelization = true)]
public sealed class StartupFailureCollection { }

[Collection("StartupFailure")]
public sealed class StartupFailureRegressionTests
{
    private const string LoadErrorTitle = "게임 정보 읽기 실패";

    [Fact]
    public Task ConstructorDataLoadFailureThenCallerShowDoesNotThrow() => Sta(() =>
    {
        var root = IsolatedRoot();
        Directory.CreateDirectory(root);
        AutomationEventHandler? handler = null;
        var dialog = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var settings = new AppSettings();
            TelemetryConsentPolicy.ApplyAgreement(settings);
            SettingsStore.SaveEnsuringDirectory(settings, Path.Combine(root, "settings.json"));
            File.WriteAllText(Path.Combine(root, "game-data.json"), "{not-valid-catalog");

            handler = (sender, _) =>
            {
                if (sender is not AutomationElement opened) return;
                if (opened.Current.ProcessId != Environment.ProcessId ||
                    opened.Current.Name != LoadErrorTitle) return;
                var text = string.Join("\n", opened.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                    .Cast<AutomationElement>().Select(element => element.Current.Name));
                dialog.TrySetResult(text);
                if (opened.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                    ?.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern invoke)
                    invoke.Invoke();
            };
            Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement, TreeScope.Subtree, handler);

            var id = Path.GetFileName(root);
            var app = new App
            {
                Execution = OverlayExecutionContext.Production(root),
                InstanceMutexName = @"Local\OrandOverlay.Tests.StartupFailure." + id,
                ActivationEventName = @"Local\OrandOverlay.Tests.StartupFailure.Show." + id
            };
            app.InitializeComponent();
            TelemetryConsentStartup.ClearStartupUri(app);
            Exception? thrown = null;
            app.DispatcherUnhandledException += (_, e) =>
            {
                thrown ??= e.Exception;
                e.Handled = true;
                app.Shutdown(1);
            };
            try { app.Run(); }
            catch (Exception error) { thrown ??= error; }

            Assert.True(dialog.Task.IsCompletedSuccessfully, "Expected the catalog load error dialog.");
            Assert.Null(thrown);
            Assert.False(app.MainWindow?.IsVisible ?? false);
            Assert.False(app.MainWindow?.IsLoaded ?? false);
        }
        finally
        {
            if (handler is not null)
                Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement, handler);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }, TimeSpan.FromSeconds(60));

    private static string IsolatedRoot() =>
        Path.Combine(Path.GetTempPath(), "randypick-startup-fail-" + Guid.NewGuid().ToString("N"));

    private static async Task Sta(Action action, TimeSpan? timeout = null)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(20));
    }
}
