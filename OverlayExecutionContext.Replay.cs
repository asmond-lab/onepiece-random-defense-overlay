namespace OrandOverlay;

internal sealed partial class OverlayExecutionContext
{
    // A native read capability, not production runtime authority. Replay never receives
    // a production user root, consent store, HTTP client, updater or telemetry outbox.
    public bool LiveMemoryEnabled => RuntimeEnabled || ReplayDirectory is not null;
    public string? ReplayDirectory { get; }

    public static OverlayExecutionContext Replay(string analysisRoot, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Path.IsPathFullyQualified(analysisRoot) ||
            Path.GetFileName(Path.TrimEndingDirectorySeparator(analysisRoot)) != ".replay-analysis-artifacts")
            throw new ArgumentException("An absolute .replay-analysis-artifacts root is required.", nameof(analysisRoot));
        var directory = Path.Combine(analysisRoot, "replay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new(false, null, Clone(settings), lines =>
            File.AppendAllLines(Path.Combine(directory, "unknown-rawcodes.log"), lines),
            new CoachJournal(Path.Combine(directory, "coach-replays")), replayDirectory: directory);
    }
}
