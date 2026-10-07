using System.Collections.Immutable;

namespace OrandOverlay;

/// <summary>Copies only allowlisted presentation facts; never changes recognition or authorizes gameplay.</summary>
internal sealed class ObservationTelemetryCapture(Func<ObservedTelemetryEvent, bool> emit, Action resetSession)
{
    private string? _context, _inventoryKey, _selected, _recommended, _presentationState;
    private readonly Dictionary<string, string> _readStates = new(StringComparer.Ordinal);
    private DateTimeOffset _lastInventory, _lastReadHeartbeat;
    private DateTimeOffset? _gapStarted;
    private bool _hasObservation;

    public void Observe(IDiagnosticInventoryReference value, long generation, NormalCandidateBrowser? browser, DateTimeOffset now, IEnumerable<string>? displayedRecommendations = null)
    {
        var context = string.Join("|", generation, value.BindingContextId.Length > 0 ? value.BindingContextId : value.ContextId, value.ViewSlot);
        if (_context is not null && _context != context) Reset();
        _context = context;
        _hasObservation = true;
        var gap = _gapStarted is { } started ? (long)Math.Max(0, (now - started).TotalMilliseconds) : (long?)null;
        PresentationState("fresh", "none", now, gap: gap, age: Milliseconds(now - value.StartedAt));
        var key = string.Join("|", value.ObservedRound, string.Join(",", value.Counts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + x.Value)));
        if (_inventoryKey != key || now - _lastInventory >= TimeSpan.FromSeconds(30))
        {
            if (emit(new() { Kind = "inventory", State = "fresh", Inventory = value.Counts,
                Round = value.ObservedRound, DurationMs = Milliseconds(value.ReadDuration),
                AgeMs = Milliseconds(now - value.StartedAt), SourceRevision = value.SourceRevision,
                Stage = browser?.ProgressStage.ToString() }))
            { _inventoryKey = key; _lastInventory = now; }
        }
        if (browser is null) return;
        var selected = browser.SelectedUnitId;
        if (selected is not null && selected != _selected && emit(new() { Kind = "selection", State = "target-selected",
            TargetUnitId = selected, Stage = browser.Stage.ToString(), Round = value.ObservedRound })) _selected = selected;
        else if (selected is null) _selected = null;
        var recommendations = (displayedRecommendations ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var recommendationKey = browser.Stage + ":" + string.Join(",", recommendations);
        if (_recommended != recommendationKey)
        {
            var recorded = true;
            foreach (var id in recommendations)
                recorded &= emit(new() { Kind = "recommendation", State = "recommended", TargetUnitId = id,
                    Stage = browser.Stage.ToString(), Round = value.ObservedRound });
            if (recorded) _recommended = recommendationKey;
        }
    }

    public void Read(bool accepted, string lane, string reason, TimeSpan duration, long? revision, DateTimeOffset now)
    {
        var state = accepted ? "fresh" : reason == "read-error" ? "read-failed" : "rejected";
        var key = state + ":" + reason;
        if (_readStates.GetValueOrDefault(lane) == key && now - _lastReadHeartbeat < TimeSpan.FromSeconds(30)) return;
        if (emit(new() { Kind = "recognition", State = state, Lane = lane, ReasonCode = reason,
            DurationMs = Milliseconds(duration), SourceRevision = revision }))
        { _readStates[lane] = key; _lastReadHeartbeat = now; }
    }

    public void PresentationState(string state, string reason, DateTimeOffset now, long? gap = null, int? age = null)
    {
        if (_presentationState == state) return;
        if (state != "fresh" && _hasObservation) _gapStarted ??= now;
        if (!emit(new() { Kind = "recognition", State = state, Lane = "presentation", ReasonCode = reason,
            GapMs = gap, AgeMs = age })) return;
        _presentationState = state;
        if (state == "fresh") _gapStarted = null;
    }

    public void Reset()
    {
        if (_context is null && !_hasObservation) return;
        emit(new() { Kind = "session", State = "session-reset" });
        resetSession();
        _context = _inventoryKey = _selected = _recommended = _presentationState = null;
        _readStates.Clear(); _gapStarted = null; _hasObservation = false;
    }

    internal static int Milliseconds(TimeSpan value) => (int)Math.Clamp(value.TotalMilliseconds, 0, 86400000);
    internal static string Reason(string reason, RecognitionState? state = null) => state switch
    {
        RecognitionState.Unsupported or RecognitionState.UnverifiedProfile => "unsupported",
        RecognitionState.ConfigurationError => "configuration",
        RecognitionState.TransientReadError => "read-error",
        RecognitionState.Waiting => "unavailable",
        _ => reason.Contains("read failed", StringComparison.OrdinalIgnoreCase) || reason.Contains("read error", StringComparison.OrdinalIgnoreCase) ? "read-error" :
            reason.Contains("stale", StringComparison.OrdinalIgnoreCase) || reason.Contains("freshness", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("budget", StringComparison.OrdinalIgnoreCase) ? "freshness" :
            reason.Contains("provenance", StringComparison.OrdinalIgnoreCase) || reason.Contains("context", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("binding", StringComparison.OrdinalIgnoreCase) ? "binding" : "unknown"
    };
}
