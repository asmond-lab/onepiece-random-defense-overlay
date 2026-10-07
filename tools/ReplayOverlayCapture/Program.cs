using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using OrandOverlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--verify-isolation"))
            throw new ArgumentException("Only --verify-isolation is accepted; normal invocation opens the replay diagnostic UI.");
        // dotnet --artifacts-path: <analysis>/bin/ReplayOverlayCapture/<configuration>/
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
        var execution = OverlayExecutionContext.Replay(root,
            new AppSettings { Mode = PlayMode.Guide, GuideNumber = 1, AutoScanEnabled = true });
        Console.WriteLine("REPLAY VERIFICATION: read-only Warcraft memory; no network or production persistence.");
        Console.WriteLine("Isolated directory: " + execution.ReplayDirectory);
        if (args.Length == 1)
        {
            VerifyIsolation(execution);
            Console.WriteLine("REPLAY_ISOLATION_PASS (factories only; no process read, game launch or UI)");
            return 0;
        }

        var app = new App { Execution = execution };
        app.InitializeComponent();
        TelemetryConsentStartup.ClearStartupUri(app);
        app.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            var window = new MainWindow(execution);
            app.MainWindow = window;
            window.Closed += async (_, _) =>
            {
                await execution.CreateCoachJournal()!.FlushAsync();
                app.Shutdown();
            };
            window.Show();
        }));
        return app.Run();
    }

    private static void VerifyIsolation(OverlayExecutionContext execution)
    {
        using var handler = new RejectNetwork();
        using var http = new HttpClient(handler);
        var catalog = execution.CreateCatalog();
        catalog.Load(loadCarryPolicy: false);
        if (execution.RuntimeEnabled || !execution.LiveMemoryEnabled || execution.HasCurrentConsent ||
            execution.CreateRecognizer(catalog) is not WarcraftMemoryRecognitionService ||
            execution.CreateGameplayTelemetry(new("0.6.70", "2.314", RouteQuestCatalog.MapScriptSha256, "2.0.4.23745"), catalog, http) is not null ||
            execution.CreateGameplayStatsRefreshService(http) is not null || execution.CreateUpdateService() is not null ||
            execution.CreateClearRefreshService() is not null || execution.CreateTelemetry(true).Enabled)
            throw new InvalidOperationException("Replay capability isolation failed.");
        execution.RefreshProfilesAsync().GetAwaiter().GetResult();
        execution.RunRuntime(() => throw new InvalidOperationException("Runtime effects leaked."));
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Replay attempted HTTP: " + request.RequestUri);
    }
}
