using System.IO;
namespace PlannerEvidenceCapture;

// A dedicated fixture-only entry contract; never forwards to the multi-mode Program.Main.
internal static class RandipickCleanupArguments
{
    public static void Validate(string[] args)
    {
        if (args.Length != 5 || args[0] != "--randipick-ui-cleanup-fixture" || args[1] != "--output" ||
            args[3] != "--candidate-sha" || args[4].Length != 64 || !args[4].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Only the bound UI cleanup fixture mode is supported.");
        var output = Path.GetFullPath(args[2]);
        var parent = Path.GetDirectoryName(output);
        if (!Path.IsPathFullyQualified(args[2]) || !string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(output).StartsWith("randipick-ui-cleanup-", StringComparison.Ordinal) || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("A new direct temporary randipick-ui-cleanup- directory is required.");
    }
}
