namespace OrandOverlay;

/// <summary>Accepts only the basic producer contract; presentation never advances its revision.</summary>
public static class DiagnosticBasicInventoryConsumerPolicy
{
    public static bool CanPresent(DiagnosticBasicInventoryObservation? value, DateTimeOffset now,
        string map, string fingerprint, string context, long acceptedRevision, int viewSlot) =>
        value is { Availability: DiagnosticInventoryAvailability.Ready } && value.HasValidPins &&
        value.SelectedMapVersion == map && value.DatasetFingerprint == fingerprint &&
        DiagnosticInventoryObservation.IsContextId(context) && value.ContextId == context &&
        DiagnosticInventoryObservation.IsContextId(value.WorldStampFingerprint) &&
        DiagnosticInventoryObservation.IsContextId(value.BindingContextId) &&
        value.SourceRevision == acceptedRevision && acceptedRevision > 0 && value.ViewSlot == viewSlot &&
        !value.GrowthAttributionAvailable && value.GrowthReservedUnitIds.IsEmpty && value.ObservedRound is null &&
        !value.GameplayReady && !value.CanProvideCoachCurrent && !value.CanProveLocalOwnership &&
        !value.CanProveAlive && !value.CanProveCompleteness && !value.CanProveRuntimeMapCurrentness &&
        Fresh(value, now);

    public static bool TryAccept(DiagnosticBasicInventoryObservation? value, RecognitionDiagnostics? diagnostics,
        DateTimeOffset now, string expectedMapVersion, string expectedDatasetFingerprint, string expectedContextId,
        long lastAcceptedRevision, int expectedViewSlot, out DiagnosticBasicInventoryObservation? observation, out string reason)
    {
        observation = null;
        reason = "Missing or unavailable typed basic diagnostic observation";
        if (value is not { Availability: DiagnosticInventoryAvailability.Ready }) return false;
        reason = "Basic diagnostic provenance/context fence rejected";
        if (diagnostics is null || diagnostics.Source != DiagnosticBasicInventoryObservation.SourceName ||
            diagnostics.ProcessVersion != value.ExecutableVersion ||
            !string.Equals(diagnostics.ExecutableSha256, value.ExecutableFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !value.HasValidPins || expectedMapVersion != value.SelectedMapVersion ||
            expectedDatasetFingerprint != value.DatasetFingerprint ||
            !DiagnosticInventoryObservation.IsContextId(expectedContextId) || value.ContextId != expectedContextId ||
            !DiagnosticInventoryObservation.IsContextId(value.WorldStampFingerprint) ||
            !DiagnosticInventoryObservation.IsContextId(value.BindingContextId) ||
            lastAcceptedRevision < 0 || value.SourceRevision <= lastAcceptedRevision || expectedViewSlot != value.ViewSlot ||
            value.GrowthAttributionAvailable || !value.GrowthReservedUnitIds.IsEmpty || value.ObservedRound is not null ||
            value.GameplayReady || value.CanProvideCoachCurrent || value.CanProveLocalOwnership || value.CanProveAlive ||
            value.CanProveCompleteness || value.CanProveRuntimeMapCurrentness) return false;
        reason = "Basic diagnostic observation stale, future-dated or over budget";
        if (!Fresh(value, now)) return false;
        observation = value;
        reason = "Bounded basic diagnostic reference only; growth attribution unavailable; GameplayReady=false";
        return true;
    }

    private static bool Fresh(DiagnosticBasicInventoryObservation value, DateTimeOffset now) =>
        value.StartedAt != default && value.CompletedAt >= value.StartedAt && value.CompletedAt <= now &&
        now - value.StartedAt < DiagnosticBasicInventoryObservation.FreshnessBudget &&
        value.ReadDuration >= TimeSpan.Zero && value.ReadDuration < DiagnosticBasicInventoryObservation.FreshnessBudget &&
        value.CompletedAt - value.StartedAt < DiagnosticBasicInventoryObservation.FreshnessBudget;
}
