namespace OrandOverlay;

internal enum AdaptivePlanningReadChannel
{
    UnitTraversal,
    MapState,
    SideChannel
}

internal sealed class AdaptivePlanningReadObserver
{
    private AdaptivePlanningReadChannel _channel;
    private AdaptivePlanningReadMetric _unit;
    private AdaptivePlanningReadMetric _map;
    private AdaptivePlanningReadMetric _side;

    public IDisposable Begin(AdaptivePlanningReadChannel channel)
    {
        var previous = _channel;
        _channel = channel;
        return new Scope(() => _channel = previous);
    }

    public void Record(long bytes, TimeSpan elapsed)
    {
        var metric = Metric(_channel);
        Set(_channel, Add(metric, bytes, 1, elapsed));
    }

    public AdaptivePlanningReadCounters Snapshot() => new(_unit, _map, _side);

    public void ReattributeUnitDelta(AdaptivePlanningReadCounters before)
    {
        var delta = Subtract(_unit, before.UnitTraversal);
        _unit = Subtract(_unit, delta);
        _side = Add(_side, delta.Bytes, delta.Calls, delta.Elapsed);
    }

    private AdaptivePlanningReadMetric Metric(AdaptivePlanningReadChannel channel) =>
        channel switch
        {
            AdaptivePlanningReadChannel.UnitTraversal => _unit,
            AdaptivePlanningReadChannel.MapState => _map,
            AdaptivePlanningReadChannel.SideChannel => _side,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };

    private void Set(AdaptivePlanningReadChannel channel, AdaptivePlanningReadMetric value)
    {
        switch (channel)
        {
            case AdaptivePlanningReadChannel.UnitTraversal: _unit = value; break;
            case AdaptivePlanningReadChannel.MapState: _map = value; break;
            case AdaptivePlanningReadChannel.SideChannel: _side = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(channel));
        }
    }

    private static AdaptivePlanningReadMetric Add(AdaptivePlanningReadMetric metric,
        long bytes, long calls, TimeSpan elapsed) => new(
        checked(metric.Bytes + bytes), checked(metric.Calls + calls), metric.Elapsed + elapsed);

    private static AdaptivePlanningReadMetric Subtract(AdaptivePlanningReadMetric left,
        AdaptivePlanningReadMetric right) => new(
        checked(left.Bytes - right.Bytes), checked(left.Calls - right.Calls),
        left.Elapsed - right.Elapsed);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed record AdaptivePlanningRecognitionObservation
{
    public AdaptivePlanningReadCounters Reads { get; }
    public TimeSpan RecognitionElapsed { get; }
    public long SideChannelByteBudget { get; }
    public long SideChannelCallBudget { get; }

    public static AdaptivePlanningRecognitionObservation Empty { get; } = new(
        AdaptivePlanningReadCounters.Empty, TimeSpan.Zero, 0, 0);

    public AdaptivePlanningRecognitionObservation(AdaptivePlanningReadCounters reads,
        TimeSpan recognitionElapsed, long sideChannelByteBudget,
        long sideChannelCallBudget)
    {
        ArgumentNullException.ThrowIfNull(reads);
        if (recognitionElapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(recognitionElapsed));
        if (sideChannelByteBudget < 0)
            throw new ArgumentOutOfRangeException(nameof(sideChannelByteBudget));
        if (sideChannelCallBudget < 0)
            throw new ArgumentOutOfRangeException(nameof(sideChannelCallBudget));
        Reads = reads;
        RecognitionElapsed = recognitionElapsed;
        SideChannelByteBudget = sideChannelByteBudget;
        SideChannelCallBudget = sideChannelCallBudget;
    }
}
