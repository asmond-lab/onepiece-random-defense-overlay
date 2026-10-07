using System.Collections.Immutable;

namespace OrandOverlay;

internal sealed record DiagnosticBasicInventorySample(
    DiagnosticBasicInventoryObservation Observation, RecognitionDiagnostics Diagnostics);

/// <summary>A fresh basic current-view read, independent of growth discovery and attribution.</summary>
public sealed class DiagnosticBasicInventoryObservation : IDiagnosticInventoryReference
{
    public const string SourceName = "WarcraftMemoryDiagnostic300BasicCurrentView";
    public static readonly TimeSpan FreshnessBudget = TimeSpan.FromSeconds(3);
    public const int MaximumEntries = DiagnosticInventoryObservation.MaximumEntries;
    public DiagnosticInventoryAvailability Availability { get; }
    public string Reason { get; }
    public DiagnosticInventoryScope Scope => DiagnosticInventoryScope.DiagnosticCurrentView;
    public DiagnosticEvidence LocalOwnership => DiagnosticEvidence.Unknown;
    public DiagnosticEvidence Alive => DiagnosticEvidence.Unknown;
    public DiagnosticEvidence Completeness => DiagnosticEvidence.Unknown;
    public DiagnosticEvidence RuntimeMapProof => DiagnosticEvidence.Unknown;
    public bool CanProveLocalOwnership => false;
    public bool CanProveAlive => false;
    public bool CanProveCompleteness => false;
    public bool GameplayReady => false;
    public bool CanProvideCoachCurrent => false;
    public bool CanProveRuntimeMapCurrentness => false;
    public bool GrowthAttributionAvailable => false;
    public string Source => SourceName;
    public string SelectedMapVersion { get; }
    public string DatasetFingerprint { get; }
    public string GrowthSourceFingerprint => "";
    public string MapSourceFingerprint => SelectedMapVersion == Map2323SourceContract.MapVersion
        ? Map2323SourceContract.JassSha256 : SelectedMapVersion == Map2322SourceContract.MapVersion
        ? Map2322SourceContract.JassSha256 : Map2320GrowthSource.JassSha256;
    public string ExecutableVersion { get; }
    public string ExecutableFingerprint { get; }
    public string ContextId { get; }
    public string WorldStampFingerprint { get; }
    public string BindingContextId { get; }
    internal ImmutableHashSet<string> GrowthUnitFingerprints { get; }
    public long SourceRevision { get; }
    public int? ViewSlot { get; }
    public int? ObservedRound => null;
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public TimeSpan ReadDuration { get; }
    public ImmutableArray<DiagnosticInventoryEntry> Entries { get; }
    public ImmutableDictionary<string, int> Counts { get; }
    public ImmutableArray<string> GrowthReservedUnitIds => [];

    private DiagnosticBasicInventoryObservation(DiagnosticInventoryAvailability availability, string reason,
        string map, string fingerprint, string version, string hash, string context, long revision, int? slot,
        DateTimeOffset started, DateTimeOffset completed, TimeSpan duration,
        ImmutableArray<DiagnosticInventoryEntry> entries, string worldStampFingerprint, string bindingContextId,
        ImmutableHashSet<string>? growthUnitFingerprints = null)
    {
        Availability = availability; Reason = reason; SelectedMapVersion = map; DatasetFingerprint = fingerprint;
        ExecutableVersion = version; ExecutableFingerprint = hash; ContextId = context; SourceRevision = revision;
        ViewSlot = slot; StartedAt = started; CompletedAt = completed; ReadDuration = duration;
        Entries = entries; Counts = entries.ToImmutableDictionary(x => x.UnitId, x => x.Count, StringComparer.Ordinal);
        WorldStampFingerprint = worldStampFingerprint; BindingContextId = bindingContextId;
        GrowthUnitFingerprints = growthUnitFingerprints ?? ImmutableHashSet<string>.Empty;
    }

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Pins(string map, string fingerprint, string version, string hash) =>
        (Map2320DataBundle.IsCompatible(map) && fingerprint == DiagnosticInventoryObservation.PinnedDatasetFingerprint ||
         map == Map2322SourceContract.MapVersion && fingerprint == DiagnosticInventoryObservation.Pinned2322DatasetFingerprint ||
         map == Map2323SourceContract.MapVersion && fingerprint == DiagnosticInventoryObservation.Pinned2323DatasetFingerprint) &&
        version == Warcraft300Diagnostic.Version && string.Equals(hash, Warcraft300Diagnostic.Hash, StringComparison.OrdinalIgnoreCase);
    internal bool HasValidPins => Pins(SelectedMapVersion, DatasetFingerprint, ExecutableVersion, ExecutableFingerprint);

    internal static DiagnosticBasicInventoryObservation Create(DataCatalog catalog, string version, string hash,
        string context, long revision, int slot, DateTimeOffset started, DateTimeOffset completed, TimeSpan duration,
        IEnumerable<InventoryEntry> entries, string worldStampFingerprint, string bindingContextId,
        IEnumerable<string>? growthUnitFingerprints = null)
    {
        var map = catalog.MapVersion;
        var fingerprint = catalog.SelectedDatasetFingerprint;
        DiagnosticBasicInventoryObservation Reject(string reason) => Unavailable(map, fingerprint, version, hash,
            revision, started, completed, duration, reason);
        if (!Pins(map, fingerprint, version, hash)) return Reject("Unapproved map/dataset/executable pins");
        if (!DiagnosticInventoryObservation.IsContextId(context) || revision <= 0 || slot is < 0 or > 23)
            return Reject("Invalid context/revision/view");
        if (!DiagnosticInventoryObservation.IsContextId(worldStampFingerprint) || !DiagnosticInventoryObservation.IsContextId(bindingContextId))
            return Reject("Invalid world/binding fingerprints");
        if (started == default || completed < started || duration < TimeSpan.Zero || duration >= FreshnessBudget ||
            completed - started >= FreshnessBudget) return Reject("Read exceeded freshness budget or invalid timestamps");
        if (entries is null) return Reject("Missing entries");
        var copy = entries.Take(MaximumEntries + 1).ToArray();
        if (copy.Length > MaximumEntries) return Reject("Inventory bound exceeded");
        var growth = (growthUnitFingerprints ?? []).Take(MaximumEntries + 1).ToArray();
        if (growth.Length > MaximumEntries || growth.Any(value => !DiagnosticInventoryObservation.IsContextId(value)))
            return Reject("Invalid growth membership fingerprints");
        var known = catalog.AllUnits.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in copy)
        {
            if (entry is null || !Text(entry.UnitId, 256) || !known.Contains(entry.UnitId) || !seen.Add(entry.UnitId) ||
                entry.Count is <= 0 or > MaximumEntries) return Reject("Invalid/unknown/duplicate inventory entry");
            total += entry.Count;
        }
        if (total > MaximumEntries) return Reject("Invalid inventory total");
        return new(DiagnosticInventoryAvailability.Ready, "Bounded basic diagnostic reference inventory only",
            map, fingerprint, version, hash, context, revision, slot, started, completed, duration,
            copy.Select(x => new DiagnosticInventoryEntry(x.UnitId, x.Count)).ToImmutableArray(), worldStampFingerprint, bindingContextId,
            growth.ToImmutableHashSet(StringComparer.Ordinal));
    }

    internal static DiagnosticBasicInventoryObservation Unavailable(string map, string fingerprint, string version,
        string hash, long revision, DateTimeOffset started, DateTimeOffset completed, TimeSpan duration, string reason) =>
        new(DiagnosticInventoryAvailability.Unavailable, Text(reason, 256) ? reason : "Basic diagnostic observation unavailable",
            Text(map, 32) ? map : "", Text(fingerprint, 64) ? fingerprint : "", Text(version, 64) ? version : "",
            Text(hash, 64) ? hash : "", "", revision, null, started, completed, duration, [], "", "");

    public List<InventoryEntry> CloneEntries() => Entries.Select(x => new InventoryEntry { UnitId = x.UnitId, Count = x.Count }).ToList();
}
