using System.Text.Json;

internal sealed class ActivityCadenceAccumulator
{
    private sealed class LaneState
    {
        internal readonly List<DateTimeOffset> Starts = [];
        internal readonly List<double> Durations = [];
        internal long Accepted;
        internal long Rejected;
        internal long Frames;
    }

    private readonly Dictionary<string, LaneState> _lanes = new(StringComparer.Ordinal)
    {
        ["basic"] = new(),
        ["full"] = new()
    };
    private readonly List<double> _renderingDurations = [];
    private long _presentations;
    private long _currentPresentations;
    private long _noncurrentPresentations;

    internal void Add(string kind, JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return;
        if (kind == "memory.read" && TryLane(data, out var readLane))
        {
            if (data.TryGetProperty("accepted", out var accepted) &&
                accepted.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (accepted.GetBoolean()) readLane.Accepted++;
                else readLane.Rejected++;
            }
            if (data.TryGetProperty("startedAt", out var started) &&
                started.ValueKind == JsonValueKind.String &&
                started.TryGetDateTimeOffset(out var startedAt))
                readLane.Starts.Add(startedAt);
            if (data.TryGetProperty("durationMs", out var duration) &&
                duration.TryGetDouble(out var durationMs) && double.IsFinite(durationMs))
                readLane.Durations.Add(durationMs);
            return;
        }
        if (kind == "frame.received" && TryLane(data, out var frameLane))
        {
            frameLane.Frames++;
            return;
        }
        if (kind != "ui.presentation") return;
        _presentations++;
        if (data.TryGetProperty("current", out var current) &&
            current.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            if (current.GetBoolean()) _currentPresentations++;
            else _noncurrentPresentations++;
        }
        if (data.TryGetProperty("renderingMs", out var rendering) &&
            rendering.TryGetDouble(out var renderingMs) && double.IsFinite(renderingMs))
            _renderingDurations.Add(renderingMs);
    }

    internal ActivityCadenceMetrics Snapshot() => new(
        Lane("basic"), Lane("full"),
        new(_presentations, _currentPresentations, _noncurrentPresentations,
            Distribution(_renderingDurations)));

    private LaneReadMetrics Lane(string name)
    {
        var state = _lanes[name];
        var starts = state.Starts.Order().ToArray();
        var intervals = new List<double>(Math.Max(0, starts.Length - 1));
        for (var index = 1; index < starts.Length; index++)
            intervals.Add((starts[index] - starts[index - 1]).TotalMilliseconds);
        return new(name, state.Accepted + state.Rejected, state.Accepted, state.Rejected,
            state.Frames, Distribution(intervals), Distribution(state.Durations));
    }

    private bool TryLane(JsonElement data, out LaneState lane)
    {
        if (data.TryGetProperty("lane", out var laneValue) &&
            laneValue.ValueKind == JsonValueKind.String &&
            laneValue.GetString() is { } laneName && _lanes.TryGetValue(laneName, out var found))
        {
            lane = found;
            return true;
        }
        lane = null!;
        return false;
    }

    private static DistributionMetrics Distribution(IReadOnlyCollection<double> source)
    {
        if (source.Count == 0) return new(0, null, null, null, null, null);
        var values = source.Order().ToArray();
        return new(values.Length, values[0], Percentile(values, 0.50),
            Percentile(values, 0.95), values[^1], values.Average());
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var position = (sorted.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}
