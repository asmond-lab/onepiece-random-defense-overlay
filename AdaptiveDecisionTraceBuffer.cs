using System.Collections.Immutable;

namespace OrandOverlay;

public enum AdaptiveDecisionAppendResult { Appended, Duplicate, Transient, Stale, ExceedsByteBudget }

public sealed class AdaptiveDecisionTraceBuffer
{
    private readonly object _gate = new();
    private readonly int _maxEventCount;
    private readonly int _maxBytes;
    private readonly List<AdaptiveDecisionEvent> _events = [];
    private int _totalBytes;
    private long _matchGeneration;

    public AdaptiveDecisionTraceBuffer(int maxEventCount = 128, int maxBytes = 131_072)
    {
        if (maxEventCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxEventCount));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxEventCount = maxEventCount;
        _maxBytes = maxBytes;
    }

    public long MatchGeneration { get { lock (_gate) return _matchGeneration; } }
    public int TotalBytes { get { lock (_gate) return _totalBytes; } }
    public ImmutableArray<AdaptiveDecisionEvent> Events { get { lock (_gate) return _events.ToImmutableArray(); } }

    public AdaptiveDecisionAppendResult TryAppend(AdaptiveDecisionEvent decision,
        string currentInputFingerprint, bool isTransient)
    {
        if (decision is null) throw new ArgumentNullException(nameof(decision));
        if (string.IsNullOrWhiteSpace(currentInputFingerprint)) throw new ArgumentException("A current input fingerprint is required.", nameof(currentInputFingerprint));
        lock (_gate)
        {
            if (isTransient) return AdaptiveDecisionAppendResult.Transient;
            if (decision.MatchGeneration != _matchGeneration || !decision.InputFingerprint.Equals(currentInputFingerprint, StringComparison.Ordinal)) return AdaptiveDecisionAppendResult.Stale;
            if (_events.Any(item => item.DecisionFingerprint.Equals(decision.DecisionFingerprint, StringComparison.Ordinal))) return AdaptiveDecisionAppendResult.Duplicate;
            var bytes = decision.SerializedBytes.Length;
            if (bytes > _maxBytes) return AdaptiveDecisionAppendResult.ExceedsByteBudget;
            while (_events.Count >= _maxEventCount || _totalBytes + bytes > _maxBytes)
            {
                _totalBytes -= _events[0].SerializedBytes.Length;
                _events.RemoveAt(0);
            }
            _events.Add(decision);
            _totalBytes += bytes;
            return AdaptiveDecisionAppendResult.Appended;
        }
    }

    public void ConfirmedMatchReset()
    {
        lock (_gate)
        {
            _events.Clear();
            _totalBytes = 0;
            _matchGeneration++;
        }
    }
}
