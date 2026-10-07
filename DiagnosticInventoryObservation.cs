using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace OrandOverlay;

public enum DiagnosticInventoryAvailability { Unavailable, Ready }
public enum DiagnosticInventoryScope { DiagnosticCurrentView }
public enum DiagnosticEvidence { Unknown }
public sealed record DiagnosticInventoryEntry(string UnitId, int Count);

/// <summary>Reference-material input only. Native allocation validity is NOT life or identity evidence.
/// No public constructor/deserializer: only the gated diagnostic producer can mint this contract.</summary>
public sealed class DiagnosticInventoryObservation : IDiagnosticInventoryReference
{
    public const string SourceName = "WarcraftMemoryDiagnostic300CurrentView";
    public static readonly TimeSpan FreshnessBudget = TimeSpan.FromSeconds(3);
    public const int MaximumEntries = 65536;
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
    public string Source => SourceName;
    public string SelectedMapVersion { get; }
    public string DatasetFingerprint { get; }
    public string GrowthSourceFingerprint => SelectedMapVersion switch
    {
        "2.322" => "cb15ba1e524c9c055384284f0715c11f16fde31fa908ad9d5779f8b5d90f7e6e",
        "2.323" => Map2323SourceContract.GrowthDataSha256,
        _ => Map2320GrowthSource.DataSha256
    };
    public string MapSourceFingerprint => SelectedMapVersion == Map2323SourceContract.MapVersion
        ? Map2323SourceContract.JassSha256 : SelectedMapVersion == Map2322SourceContract.MapVersion
        ? Map2322SourceContract.JassSha256 : Map2320GrowthSource.JassSha256;
    public string ExecutableVersion { get; }
    public string ExecutableFingerprint { get; }
    public string ContextId { get; }
    public string WorldStampFingerprint { get; }
    public string BindingContextId { get; }
    internal string GrowthUnitFingerprint { get; }
    public bool GrowthAttributionAvailable => Availability == DiagnosticInventoryAvailability.Ready;
    public long SourceRevision { get; }
    public int? ViewSlot { get; }
    // Source-pair / owned-title reference only. Never feeds gameplay phase or coaching.
    public int? ObservedRound { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public TimeSpan ReadDuration { get; }
    public ImmutableArray<DiagnosticInventoryEntry> Entries { get; }
    public ImmutableDictionary<string, int> Counts { get; }
    // Reservation metadata for EXISTING entries, never an extra card/resource.
    public ImmutableArray<string> GrowthReservedUnitIds { get; }

    private DiagnosticInventoryObservation(DiagnosticInventoryAvailability availability, string reason,
        string map, string fingerprint, string executableVersion, string executableFingerprint,
        string context, long revision, int? slot, DateTimeOffset started, DateTimeOffset completed,
        TimeSpan duration, ImmutableArray<DiagnosticInventoryEntry> entries, ImmutableArray<string> growth, int? observedRound = null,
        string worldStampFingerprint = "", string bindingContextId = "", string growthUnitFingerprint = "")
    {
        Availability = availability; Reason = reason; SelectedMapVersion = map; DatasetFingerprint = fingerprint;
        ExecutableVersion = executableVersion; ExecutableFingerprint = executableFingerprint;
        ContextId = context; SourceRevision = revision; ViewSlot = slot; StartedAt = started;
        WorldStampFingerprint = worldStampFingerprint; BindingContextId = bindingContextId;
        GrowthUnitFingerprint = growthUnitFingerprint;
        ObservedRound = availability == DiagnosticInventoryAvailability.Ready && observedRound is >= 1 and <= 65 ? observedRound : null;
        CompletedAt = completed; ReadDuration = duration; Entries = entries; GrowthReservedUnitIds = growth;
        Counts = entries.ToImmutableDictionary(x => x.UnitId, x => x.Count, StringComparer.Ordinal);
    }

    internal static string PinnedDatasetFingerprint => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", Map2320DataBundle.ExpectedMembers.Select(x => x.Sha256))))).ToLowerInvariant();
    internal static string Pinned2322DatasetFingerprint => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", Map2322DataBundle.ExpectedMembers.Select(x => x.Sha256))))).ToLowerInvariant();
    internal static string Pinned2323DatasetFingerprint => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", Map2322DataBundle.Expected2323Members.Select(x => x.Sha256))))).ToLowerInvariant();
    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
    internal static bool IsContextId(string? value) => value?.Length == 64 && value.All(char.IsAsciiHexDigit);
    private static bool Pins(string map, string fingerprint, string version, string hash) =>
        (Map2320DataBundle.IsCompatible(map) && fingerprint == PinnedDatasetFingerprint ||
         map == Map2322SourceContract.MapVersion && fingerprint == Pinned2322DatasetFingerprint ||
         map == Map2323SourceContract.MapVersion && fingerprint == Pinned2323DatasetFingerprint) &&
        version == Warcraft300Diagnostic.Version && string.Equals(hash, Warcraft300Diagnostic.Hash, StringComparison.OrdinalIgnoreCase);

    internal static DiagnosticInventoryObservation Create(DataCatalog catalog, string version, string hash,
        string context, long revision, int slot, DateTimeOffset started, DateTimeOffset completed, TimeSpan duration,
        IEnumerable<InventoryEntry> entries, IEnumerable<string> growthIds, int? observedRound = null,
        string worldStampFingerprint = "", string bindingContextId = "", string growthUnitFingerprint = "")
    {
        var map = catalog.MapVersion;
        var fingerprint = catalog.SelectedDatasetFingerprint;
        DiagnosticInventoryObservation Reject(string reason) => Unavailable(map, fingerprint, version, hash,
            revision, started, completed, duration, reason);
        if (!Pins(map, fingerprint, version, hash)) return Reject("Unapproved map/dataset/executable pins");
        if (!IsContextId(context) || revision <= 0 || slot is < 0 or > 23) return Reject("Invalid context/revision/view");
        if (started == default || completed < started || duration < TimeSpan.Zero ||
            duration >= FreshnessBudget || completed - started >= FreshnessBudget)
            return Reject("Read exceeded freshness budget or invalid timestamps");
        if ((worldStampFingerprint != "" || bindingContextId != "") &&
            (!IsContextId(worldStampFingerprint) || !IsContextId(bindingContextId)))
            return Reject("Invalid world/binding fingerprints");
        if (entries is null || growthIds is null) return Reject("Missing entries/reservations");
        var copy = entries.Take(MaximumEntries + 1).ToArray();
        var growth = growthIds.Take(MaximumEntries + 1).ToArray();
        if (growthUnitFingerprint != "" && (!IsContextId(growthUnitFingerprint) || growth.Length != 1 ||
            !IsContextId(worldStampFingerprint) || !IsContextId(bindingContextId)))
            return Reject("Invalid projected growth fingerprint");
        var known = catalog.AllUnits.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        if (copy.Length > MaximumEntries || growth.Length > MaximumEntries) return Reject("Inventory bound exceeded");
        foreach (var entry in copy)
        {
            if (entry is null || !Text(entry.UnitId, 256) || !known.Contains(entry.UnitId) ||
                !seen.Add(entry.UnitId) || entry.Count is <= 0 or > MaximumEntries)
                return Reject("Invalid/unknown/duplicate inventory entry");
            total += entry.Count;
        }
        if (total > MaximumEntries || growth.Any(id => !Text(id, 256) || !seen.Contains(id)) ||
            growth.Distinct(StringComparer.Ordinal).Count() != growth.Length)
            return Reject("Invalid total/reservation references");
        return new(DiagnosticInventoryAvailability.Ready, "Bounded diagnostic reference inventory only", map,
            fingerprint, version, hash, context, revision, slot, started, completed, duration,
            copy.Select(x => new DiagnosticInventoryEntry(x.UnitId, x.Count)).ToImmutableArray(), growth.ToImmutableArray(), observedRound,
            worldStampFingerprint, bindingContextId, growthUnitFingerprint);
    }

    internal static DiagnosticInventoryObservation Unavailable(string map, string fingerprint, string version,
        string hash, long revision, DateTimeOffset started, DateTimeOffset completed, TimeSpan duration, string reason) =>
        new(DiagnosticInventoryAvailability.Unavailable, Text(reason, 256) ? reason : "Diagnostic observation unavailable",
            Text(map, 32) ? map : "", Text(fingerprint, 64) ? fingerprint : "", Text(version, 64) ? version : "",
            Text(hash, 64) ? hash : "", "", revision, null, started, completed, duration, [], []);

    // Called only after presentation has revalidated this exact supplement in the current basic snapshot.
    // Keep the attribution's original age/revision; a basic read cannot renew growth evidence.
    internal DiagnosticInventoryObservation WithCurrentBasic(DiagnosticBasicInventoryObservation basic)
    {
        var id = GrowthReservedUnitIds.Single();
        var entries = basic.Entries.Select(entry => entry.UnitId == id ? entry with { Count = entry.Count + 1 } : entry)
            .ToImmutableArray();
        if (!basic.Counts.ContainsKey(id)) entries = entries.Add(new(id, 1));
        return new(Availability, Reason, SelectedMapVersion, DatasetFingerprint, ExecutableVersion, ExecutableFingerprint,
            ContextId, SourceRevision, ViewSlot, StartedAt, CompletedAt, ReadDuration, entries, GrowthReservedUnitIds,
            ObservedRound, basic.WorldStampFingerprint, BindingContextId, GrowthUnitFingerprint);
    }

    internal bool HasValidPins => Pins(SelectedMapVersion, DatasetFingerprint, ExecutableVersion, ExecutableFingerprint);
    public List<InventoryEntry> CloneEntries() => Entries.Select(x => new InventoryEntry { UnitId = x.UnitId, Count = x.Count }).ToList();
}

/// <summary>Pure acceptance, no state mutation or gameplay authorization. Caller owns current context/revision fences.
/// Clear the reference display on ANY rejection, missing contract, map change or non-diagnostic result.
/// Never bootstrap expectedContextId from a delayed callback: use the current serialized producer result.</summary>
public static class DiagnosticInventoryConsumerPolicy
{
    // Presentation checks an already accepted identity, never calls TryAccept or advances a revision.
    public static bool CanPresent(DiagnosticInventoryObservation? value, DateTimeOffset now,
        string map, string fingerprint, string context, long acceptedRevision, int viewSlot) =>
        value is { Availability: DiagnosticInventoryAvailability.Ready } && value.HasValidPins &&
        value.SelectedMapVersion == map && value.DatasetFingerprint == fingerprint &&
        value.ContextId == context && value.SourceRevision == acceptedRevision && value.ViewSlot == viewSlot &&
        !value.GameplayReady && !value.CanProvideCoachCurrent && !value.CanProveLocalOwnership &&
        !value.CanProveAlive && !value.CanProveCompleteness && !value.CanProveRuntimeMapCurrentness &&
        value.StartedAt != default && value.CompletedAt >= value.StartedAt && value.CompletedAt <= now &&
        now - value.StartedAt < DiagnosticInventoryObservation.FreshnessBudget &&
        value.ReadDuration >= TimeSpan.Zero && value.ReadDuration < DiagnosticInventoryObservation.FreshnessBudget &&
        value.CompletedAt - value.StartedAt < DiagnosticInventoryObservation.FreshnessBudget;

    public static bool TryAccept(RecognitionResult? result, DateTimeOffset now, string expectedMapVersion,
        string expectedDatasetFingerprint, string expectedContextId, long lastAcceptedRevision, int expectedViewSlot,
        out DiagnosticInventoryObservation? observation, out string reason)
    {
        observation = null;
        var value = result?.DiagnosticObservation;
        reason = "Missing or unavailable typed diagnostic observation";
        if (value is null || value.Availability != DiagnosticInventoryAvailability.Ready) return false;
        reason = "Diagnostic provenance/context fence rejected";
        if (result!.State != RecognitionState.Ready || result.Diagnostics is null || result.Diagnostics.Source != DiagnosticInventoryObservation.SourceName ||
            result.Diagnostics.ProcessVersion != value.ExecutableVersion ||
            !string.Equals(result.Diagnostics.ExecutableSha256, value.ExecutableFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !value.HasValidPins || expectedMapVersion != value.SelectedMapVersion ||
            expectedDatasetFingerprint != value.DatasetFingerprint || !DiagnosticInventoryObservation.IsContextId(expectedContextId) ||
            value.ContextId != expectedContextId || lastAcceptedRevision < 0 || value.SourceRevision <= lastAcceptedRevision ||
            expectedViewSlot != value.ViewSlot || value.GameplayReady || value.CanProvideCoachCurrent || value.CanProveRuntimeMapCurrentness)
            return false;
        reason = "Diagnostic observation stale, future-dated or over budget";
        if (value.StartedAt == default || value.CompletedAt < value.StartedAt || value.CompletedAt > now ||
            now - value.StartedAt >= DiagnosticInventoryObservation.FreshnessBudget ||
            value.ReadDuration < TimeSpan.Zero || value.ReadDuration >= DiagnosticInventoryObservation.FreshnessBudget ||
            value.CompletedAt - value.StartedAt >= DiagnosticInventoryObservation.FreshnessBudget) return false;
        observation = value;
        // ModelCurrentProvenance is this bounded reference acceptance, NEVER GameplayReady.
        reason = "ModelCurrentProvenance: bounded diagnostic reference only; GameplayReady=false";
        return true;
    }
}
