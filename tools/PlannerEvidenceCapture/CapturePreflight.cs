namespace PlannerEvidenceCapture;

public static class CapturePreflight
{
    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal)
    {
        "--bullet-guide", "--coach-app", "--coach-difficulties", "--coach-finished-rewards",
        "--coach-recipes", "--coach-rewards", "--coach-showcase", "--coach-status", "--four-modes",
        "--gaban-actions", "--goal-consistency", "--intermediate-craft", "--navigation-only",
        "--ready-boundary"
    };

    public static void ValidateArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var modes = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!seen.Add(arg)) throw new ArgumentException("Duplicate option.");
            if (arg is "--output" or "--build-sha")
            {
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Option value required.");
            }
            else if (!Modes.Contains(arg))
                throw new ArgumentException("Unapproved mode: live readers and path-based journals are not permitted in this fixture harness.");
            else if (++modes > 1) throw new ArgumentException("Use a separate fresh output for each capture mode.");
        }
        if (!seen.Contains("--output")) throw new ArgumentException("--output is required.");
    }
}
