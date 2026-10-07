namespace OrandOverlay;

// Pure identity fence. Deliberately carries no native address, executable path, or memory accessor.
internal sealed class ExpectedReadTarget
{
    internal int ProcessId { get; }
    internal long StartedAtUtcTicks { get; }
    internal ExpectedReadTarget(int processId, DateTimeOffset startedAt)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (startedAt == default || startedAt.UtcTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(startedAt));
        ProcessId = processId;
        StartedAtUtcTicks = startedAt.UtcTicks;
    }
    internal bool Matches(int processId, long startedAtUtcTicks) =>
        processId == ProcessId && startedAtUtcTicks == StartedAtUtcTicks;
    internal void EnsureMatches(int processId, long startedAtUtcTicks)
    {
        if (!Matches(processId, startedAtUtcTicks)) throw new ReadTargetMismatchException();
    }
}

// Not swallowed by the production reader's ordinary transient-read exception filter.
internal sealed class ReadTargetMismatchException : Exception
{
    internal ReadTargetMismatchException() : base("Explicit read target identity mismatch") { }
}
