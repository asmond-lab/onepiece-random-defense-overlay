namespace PlannerEvidenceCapture;
internal static class FastUniqueUiArguments
{
    internal static void Validate(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != 4 || args[0] != "--output" || args[2] != "--build-sha")
            throw new ArgumentException("Dedicated focused fixture accepts only --output PATH --build-sha BINDING.");
        CapturePreflight.ValidateArguments(args);
    }
}
