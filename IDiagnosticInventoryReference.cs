using System.Collections.Immutable;

namespace OrandOverlay;

/// <summary>Presentation fields shared by independently accepted reference observations; never a gameplay input.</summary>
public interface IDiagnosticInventoryReference
{
    DiagnosticInventoryAvailability Availability { get; }
    string Reason { get; }
    DiagnosticInventoryScope Scope { get; }
    DiagnosticEvidence LocalOwnership { get; }
    DiagnosticEvidence Alive { get; }
    DiagnosticEvidence Completeness { get; }
    DiagnosticEvidence RuntimeMapProof { get; }
    bool CanProveLocalOwnership { get; }
    bool CanProveAlive { get; }
    bool CanProveCompleteness { get; }
    bool GameplayReady { get; }
    bool CanProvideCoachCurrent { get; }
    bool CanProveRuntimeMapCurrentness { get; }
    string Source { get; }
    string SelectedMapVersion { get; }
    string DatasetFingerprint { get; }
    string GrowthSourceFingerprint { get; }
    string MapSourceFingerprint { get; }
    string ExecutableVersion { get; }
    string ExecutableFingerprint { get; }
    string ContextId { get; }
    string WorldStampFingerprint { get; }
    string BindingContextId { get; }
    long SourceRevision { get; }
    int? ViewSlot { get; }
    int? ObservedRound { get; }
    DateTimeOffset StartedAt { get; }
    DateTimeOffset CompletedAt { get; }
    TimeSpan ReadDuration { get; }
    ImmutableArray<DiagnosticInventoryEntry> Entries { get; }
    ImmutableDictionary<string, int> Counts { get; }
    bool GrowthAttributionAvailable { get; }
    ImmutableArray<string> GrowthReservedUnitIds { get; }
    List<InventoryEntry> CloneEntries();
}
