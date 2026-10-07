using System.Collections.Immutable;

namespace OrandOverlay;

internal sealed class LocalActivityCapture(
    DataCatalog catalog, Action<string, object, long?, long?> emit)
{
    private readonly Dictionary<string, (IDiagnosticInventoryReference Value,
        ImmutableDictionary<string, int>? Rawcodes, HashSet<string> Unknown)> _previous = new(StringComparer.Ordinal);
    private string? _context;
    private int? _round;

    internal void Observe(string lane, IDiagnosticInventoryReference value,
        RecognitionDiagnostics diagnostics, bool accepted, string reason, long generation)
    {
        emit("memory.read", new
        {
            Lane = lane, Accepted = accepted, Reason = reason, diagnostics.Detail,
            value.StartedAt, value.CompletedAt, DurationMs = value.ReadDuration.TotalMilliseconds,
            value.SourceRevision, value.SelectedMapVersion, value.DatasetFingerprint,
            value.BindingContextId, value.WorldStampFingerprint, value.ViewSlot,
            Inventory = value.Counts, Rawcodes = diagnostics.ActivityRawcodes,
            ProjectedRawcodes = diagnostics.ActivityProjectedRawcodes,
            Counters = diagnostics.ActivityCounters, CounterStatus = diagnostics.ActivityCounterStatus,
            diagnostics.ActivityUnavailableCounterNames,
            diagnostics.ActivityCounterReadCalls,
            diagnostics.ActivityCounterReadBytes,
            diagnostics.ActivityCounterReadDurationMs,
            diagnostics.ObservedObjects, diagnostics.ForeignObjects,
            UnknownRawcodes = diagnostics.UnknownRawcodes.ToArray(),
            Evidence = "observed-current-view", ActionConfirmed = false
        }, generation, value.SourceRevision);
        if (!accepted)
        {
            Gap(lane, reason, generation);
            return;
        }

        var context = string.Join("|", generation, value.SelectedMapVersion, value.DatasetFingerprint,
            value.BindingContextId.Length > 0 ? value.BindingContextId : value.ContextId, value.ViewSlot);
        if (_context != context)
        {
            _previous.Clear();
            _round = null;
            emit("game.context", new
            {
                PreviousContext = _context, Context = context, value.ViewSlot,
                Evidence = "diagnostic-context", LocalPlayerIdentityConfirmed = false
            }, generation, value.SourceRevision);
            _context = context;
        }

        var changed = true;
        if (_previous.TryGetValue(lane, out var previous))
        {
            var added = Difference(previous.Value.Counts, value.Counts);
            var removed = Difference(value.Counts, previous.Value.Counts);
            changed = added.Count > 0 || removed.Count > 0;
            if (changed)
                emit("inventory.delta", new
                {
                    Lane = lane, BeforeRevision = previous.Value.SourceRevision,
                    AfterRevision = value.SourceRevision, From = previous.Value.StartedAt, To = value.CompletedAt,
                    Added = added, Removed = removed, Evidence = "observed-current-view",
                    ActionConfirmed = false, Unattributed = true
                }, generation, value.SourceRevision);
            if (previous.Rawcodes is { } oldRaw && diagnostics.ActivityRawcodes is { } newRaw)
            {
                var hidden = new HashSet<string>(diagnostics.UnknownRawcodes, StringComparer.Ordinal);
                hidden.UnionWith(previous.Unknown);
                var rawAdded = Visible(Difference(oldRaw, newRaw), hidden);
                var rawRemoved = Visible(Difference(newRaw, oldRaw), hidden);
                if (rawAdded.Count > 0 || rawRemoved.Count > 0)
                    emit("rawcode.delta", new
                    {
                        Lane = lane, BeforeRevision = previous.Value.SourceRevision,
                        AfterRevision = value.SourceRevision, Added = rawAdded, Removed = rawRemoved,
                        Evidence = "validated-current-view-counts", ActionConfirmed = false
                    }, generation, value.SourceRevision);
            }
        }
        if (changed)
            emit("inventory.snapshot", new
            {
                Lane = lane, value.ViewSlot,
                Units = value.Entries.Select(entry => new
                {
                    entry.UnitId, Name = catalog.Unit(entry.UnitId).Name, entry.Count
                }).ToArray(),
                value.GrowthAttributionAvailable, value.GrowthReservedUnitIds,
                Evidence = "observed-current-view"
            }, generation, value.SourceRevision);
        if (value.ObservedRound is { } round && round != _round)
        {
            emit("round.observed", new { Previous = _round, Round = round, Evidence = "reference-only" },
                generation, value.SourceRevision);
            _round = round;
        }
        _previous[lane] = (value, diagnostics.ActivityRawcodes,
            new HashSet<string>(diagnostics.UnknownRawcodes, StringComparer.Ordinal));
    }

    internal void Gap(string reason, long generation)
    {
        _previous.Clear();
        emit("observation.gap", new { Reason = reason, CorrelationReset = true }, generation, null);
    }

    private void Gap(string lane, string reason, long generation)
    {
        _previous.Remove(lane);
        emit("observation.gap", new
        {
            Lane = lane,
            Reason = reason,
            LaneReset = true,
            CorrelationReset = false
        }, generation, null);
    }

    private static ImmutableDictionary<string, int> Difference(
        IReadOnlyDictionary<string, int> before, IReadOnlyDictionary<string, int> after) =>
        after.Where(pair => pair.Value > before.GetValueOrDefault(pair.Key))
            .ToImmutableDictionary(pair => pair.Key, pair => pair.Value - before.GetValueOrDefault(pair.Key),
                StringComparer.Ordinal);

    // Map() leaves controller/helper CUnits as unknown; they are not hand cards.
    private static ImmutableDictionary<string, int> Visible(
        ImmutableDictionary<string, int> delta, HashSet<string> unknown) =>
        unknown.Count == 0 ? delta
            : delta.Where(pair => !unknown.Contains(pair.Key))
                .ToImmutableDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
