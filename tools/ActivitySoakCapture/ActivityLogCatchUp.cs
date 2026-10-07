internal static class ActivityLogCatchUp
{
    internal static void Queue(
        string directory, IEnumerable<string> tracked, Action<string> enqueue)
    {
        foreach (var path in tracked
            .Concat(Directory.GetFiles(directory, "activity-*.jsonl"))
            .Distinct(StringComparer.OrdinalIgnoreCase))
            enqueue(path);
    }
}
