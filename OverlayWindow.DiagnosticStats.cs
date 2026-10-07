namespace OrandOverlay;

public partial class OverlayWindow
{
    private DataCatalog? _diagnosticStatsCatalog;
    private InventoryStatsCalculator? _diagnosticStatsCalculator;
    private InventoryStatSummary? _diagnosticStats;
    private IDiagnosticInventoryReference? _lastReadyObservation;
    private string _diagnosticStatsKey = "";

    internal void SetDiagnosticStats(IDiagnosticInventoryReference? observation, Func<bool> current, DataCatalog catalog,
        string direction = "unknown", string? anchorName = null, bool retainLastKnown = false)
    {
        Dispatcher.VerifyAccess();
        Stats.SetDiagnosticDirection(direction, anchorName);
        if (observation is not (DiagnosticInventoryObservation or DiagnosticBasicInventoryObservation) ||
            observation.Availability != DiagnosticInventoryAvailability.Ready || !current())
        {
            if (retainLastKnown && _diagnosticStats is not null && _lastReadyObservation is not null)
            {
                Stats.SetDiagnosticReference(_lastReadyObservation, () => false, _diagnosticStats);
                RenderCurrentStats(_diagnosticStats, direction == "magical", GoroseiMode.None, 1.4, 1.5,
                    allCombatStats: true, diagnosticDirection: direction);
                return;
            }
            ClearDiagnosticStats();
            return;
        }
        if (!ReferenceEquals(_diagnosticStatsCatalog, catalog))
        {
            _diagnosticStatsCatalog = catalog;
            _diagnosticStatsCalculator = new InventoryStatsCalculator(catalog);
            _diagnosticStatsKey = "";
        }
        var key = string.Join(",", observation.Entries.OrderBy(e => e.UnitId, StringComparer.Ordinal)
            .Select(e => e.UnitId + ":" + e.Count));
        if (_diagnosticStats is null || _diagnosticStatsKey != key)
        {
            _diagnosticStats = _diagnosticStatsCalculator!.Calculate(observation.CloneEntries());
            _diagnosticStatsKey = key;
        }
        _lastReadyObservation = observation;
        Stats.SetDiagnosticReference(observation, current, _diagnosticStats);
        RenderCurrentStats(_diagnosticStats, direction == "magical", GoroseiMode.None, 1.4, 1.5, allCombatStats: true, diagnosticDirection: direction);
    }
    internal void ClearDiagnosticStats()
    {
        Dispatcher.VerifyAccess();
        _diagnosticStatsKey = "";
        _diagnosticStats = null;
        _lastReadyObservation = null;
        Stats.SetDiagnosticReference(null, () => false);
    }
}
