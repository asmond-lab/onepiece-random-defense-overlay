namespace OrandOverlay;

// Dispatcher-owned reference observations; neither lane grants gameplay authority.
internal sealed class DiagnosticInventoryPresentationState
{
    private DiagnosticBasicInventoryObservation? _basic;
    private DiagnosticInventoryObservation? _full;
    private string _basicContext = "";
    private string _fullContext = "";
    private int _basicView = -1;
    private int _fullView = -1;
    private long _basicRevision;
    private long _fullRevision;
    internal bool HasBasicFrames { get; private set; }

    internal IDiagnosticInventoryReference? Current(DateTimeOffset now, string map, string fingerprint)
    {
        if (HasBasicFrames && !DiagnosticBasicInventoryConsumerPolicy.CanPresent(_basic, now, map,
                fingerprint, _basicContext, _basicRevision, _basicView))
        {
            _basic = null; _full = null;
            return null;
        }
        if (!DiagnosticInventoryConsumerPolicy.CanPresent(_full, now, map, fingerprint, _fullContext,
                _fullRevision, _fullView) || HasBasicFrames && !CanContinue(_basic, _full))
            _full = null;
        return _full is not null ? _full : _basic;
    }

    internal bool TryAcceptBasic(DiagnosticBasicInventoryObservation? value, RecognitionDiagnostics diagnostics,
        DateTimeOffset now, string map, string fingerprint, out string reason)
    {
        HasBasicFrames = true;
        var slot = value?.ViewSlot ?? -1;
        if (!DiagnosticBasicInventoryConsumerPolicy.TryAccept(value, diagnostics, now, map, fingerprint,
                value?.ContextId ?? "", _basicRevision, slot, out var accepted, out reason))
            return false;
        _basic = accepted;
        _basicContext = accepted!.ContextId; _basicRevision = accepted.SourceRevision; _basicView = slot;
        if (!CanContinue(_basic, _full)) _full = null;
        else if (_full is not null && !Matches(_basic, _full))
            _full = _full.WithCurrentBasic(accepted); // Stable identity for repeated presentation fence checks.
        return true;
    }

    internal bool TryAcceptFull(RecognitionResult result, DateTimeOffset now, string map, string fingerprint, out string reason)
    {
        reason = "Missing or unavailable typed diagnostic observation";
        if (result.ConfirmsSessionBoundary || result.State is RecognitionState.Unsupported or RecognitionState.UnverifiedProfile or
            RecognitionState.ConfigurationError)
        {
            Invalidate(result.ConfirmsSessionBoundary);
            return false;
        }
        if (result.Diagnostics.Source != DiagnosticInventoryObservation.SourceName)
            return KeepExistingFull(now, map, fingerprint, ref reason);
        var value = result.DiagnosticObservation;
        var slot = value?.ViewSlot ?? -1;
        if (!DiagnosticInventoryConsumerPolicy.TryAccept(result, now, map, fingerprint,
                value?.ContextId ?? "", _fullRevision, slot, out var accepted, out reason))
            return KeepExistingFull(now, map, fingerprint, ref reason);
        if (result.CompletionBasicSample is { } sample &&
            (!Matches(sample.Observation, accepted) || !TryAcceptBasic(sample.Observation, sample.Diagnostics,
                now, map, fingerprint, out reason)))
        {
            if (KeepExistingFull(now, map, fingerprint, ref reason)) return false;
            reason = "Completion basic inventory is invalid, stale or mismatched";
            return false;
        }
        _fullContext = accepted!.ContextId; _fullRevision = accepted.SourceRevision; _fullView = slot;
        if (HasBasicFrames && (!DiagnosticBasicInventoryConsumerPolicy.CanPresent(_basic, now, map, fingerprint,
                _basicContext, _basicRevision, _basicView) || !Matches(_basic, accepted)))
        {
            _full = null;
            reason = "Growth reference does not match the current basic inventory";
            return false;
        }
        _full = accepted;
        return true;
    }

    // Transient full fences (handle table, vtable, QR allocation race) must not erase a
    // still-fresh QR supplement the current basic still witnesses.
    private bool KeepExistingFull(DateTimeOffset now, string map, string fingerprint, ref string reason)
    {
        if (_full is not null && DiagnosticInventoryConsumerPolicy.CanPresent(_full, now, map, fingerprint,
                _fullContext, _fullRevision, _fullView) && (!HasBasicFrames || CanContinue(_basic, _full)))
            return false;
        _full = null;
        return false;
    }

    // Full acceptance/completion pairing stays exact. Only an already accepted, still-fresh
    // QR supplement may cross world changes, when its entire neutral unit identity is re-observed.
    private static bool CanContinue(DiagnosticBasicInventoryObservation? basic, DiagnosticInventoryObservation? full) =>
        Matches(basic, full) || basic is not null && full is not null &&
        basic.BindingContextId == full.BindingContextId && basic.ViewSlot == full.ViewSlot &&
        full.GrowthUnitFingerprint.Length > 0 && basic.GrowthUnitFingerprints.Contains(full.GrowthUnitFingerprint) &&
        basic.Entries.Sum(entry => entry.Count) < DiagnosticInventoryObservation.MaximumEntries;

    private static bool Matches(DiagnosticBasicInventoryObservation? basic, DiagnosticInventoryObservation? full) =>
        basic is not null && full is not null && basic.BindingContextId == full.BindingContextId &&
        basic.WorldStampFingerprint == full.WorldStampFingerprint && basic.ViewSlot == full.ViewSlot;

    internal void Invalidate(bool resetContext = false)
    {
        _basic = null; _full = null;
        if (!resetContext) return;
        _basicContext = _fullContext = "";
        _basicView = _fullView = -1;
    }
}
