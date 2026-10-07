using System.Reflection;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static void CapturePlayerResources(string output, RecognitionResult observation)
    {
        if (observation.State != RecognitionState.Ready || observation.PlayerResources is not { } resources)
            throw new InvalidOperationException("Current native resources required for UI proof.");
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var main = new MainWindow(FixtureContext(new AppSettings { Mode = PlayMode.Normal, TelemetryEnabled = false }));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var overlay = typeof(MainWindow).GetField("_overlay", flags)?.GetValue(main) as OverlayWindow
            ?? throw new InvalidOperationException("Overlay fixture unavailable.");

        try
        {
            main.Show();
            var rendered = WaitCoach(main,
                () => _ = ScanFixtureAsync(main, observation),
                (_, frame) => frame.Signals.GetValueOrDefault("gold") == resources.Gold);
            if (rendered.Frame.Signals.GetValueOrDefault("lumber") != resources.Lumber ||
                rendered.Frame.Signals.GetValueOrDefault("trait-points") != resources.TraitPoints)
                throw new InvalidOperationException("Native resource projection disagrees with coach frame.");
            overlay.Show();
            main.UpdateLayout();
            overlay.UpdateLayout();
            SaveCoachWindow(main, Path.Combine(output, "native-resources-main.png"));
            SaveCoachWindow(overlay, Path.Combine(output, "native-resources-overlay.png"));
            WriteEvidenceText(Path.Combine(output, "native-resource-ui.json"),
                JsonSerializer.Serialize(new { Native = resources, rendered.Frame.Signals,
                    rendered.Frame.IsCurrent, rendered.Decision.Kind }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PLAYER_RESOURCE_UI PASS gold={resources.Gold} lumber={resources.Lumber} traits={resources.TraitPoints}");
        }
        finally
        {
            main.Close();
            overlay.Stats.CloseForApplication();
            overlay.CloseForApplication();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
