using System.Reflection;
using OrandOverlay;

namespace PlannerEvidenceCapture;

internal static partial class Program
{
    private static OverlayExecutionContext FixtureContext(AppSettings settings) =>
        OverlayExecutionContext.Fixture(CaptureInputContract.ValidateSettings(settings));

    private static OverlayExecutionContext FixtureJournalContext(AppSettings settings) =>
        OverlayExecutionContext.FixtureWithMemoryJournal(CaptureInputContract.ValidateSettings(settings));

    private static Task ScanFixtureAsync(MainWindow window, RecognitionResult result) =>
        window.ScanControlledAsync(CaptureInputContract.ValidateRecognition(result));

    // The fixture context deep-clones its input. Assertions and mutations after window
    // construction must observe the window-owned instance, not that original input.
    private static AppSettings FixtureSettings(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var context = (OverlayExecutionContext)typeof(MainWindow).GetField("_execution", flags)!.GetValue(window)!;
        if (context.RuntimeEnabled) throw new InvalidOperationException("Fixture window required.");
        return (AppSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
    }

    private static CoachJournal FixtureJournal(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var context = (OverlayExecutionContext)typeof(MainWindow).GetField("_execution", flags)!.GetValue(window)!;
        if (context.RuntimeEnabled) throw new InvalidOperationException("Fixture window required.");
        var journal = (CoachJournal?)typeof(MainWindow).GetField("_coachJournal", flags)!.GetValue(window);
        if (journal is null || journal.IsPersistent)
            throw new InvalidOperationException("In-memory fixture journal required.");
        return journal;
    }
}
