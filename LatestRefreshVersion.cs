namespace OrandOverlay;

public sealed class LatestRefreshVersion
{
    private long _version;

    public long Next() => Interlocked.Increment(ref _version);

    public bool IsCurrent(long version) => version == Volatile.Read(ref _version);
}
