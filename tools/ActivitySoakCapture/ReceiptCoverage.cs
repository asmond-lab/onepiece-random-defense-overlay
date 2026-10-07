internal static class ReceiptCoverage
{
    internal static double MaximumGapSeconds(
        DateTimeOffset startedAtUtc,
        IReadOnlyList<DateTimeOffset> receipts,
        DateTimeOffset completedAtUtc)
    {
        var previous = startedAtUtc;
        var maximum = TimeSpan.Zero;
        foreach (var receipt in receipts)
        {
            if (receipt < previous)
                throw new InvalidOperationException("Receipt clock moved backwards.");
            maximum = Max(maximum, receipt - previous);
            previous = receipt;
        }
        if (completedAtUtc < previous)
            throw new InvalidOperationException("Completion clock moved backwards.");
        return Max(maximum, completedAtUtc - previous).TotalSeconds;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;
}
