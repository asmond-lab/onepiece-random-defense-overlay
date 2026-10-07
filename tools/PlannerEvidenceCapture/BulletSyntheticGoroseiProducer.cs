using OrandOverlay;
namespace PlannerEvidenceCapture;

// Pure extraction of BulletGuideCapture's detailed diagnostics producer. No WPF execution.
internal static class BulletSyntheticGoroseiProducer
{
    internal static RecognitionDiagnostics Diagnostics(int round, IReadOnlyList<InventoryEntry> entries,
        GoroseiMode gorosei, GoroseiMarkerSnapshot? marker,
        OverlayExecutionContext.SyntheticGoroseiInput? synthetic) => new()
    {
        Gorosei = gorosei,
        GoroseiMarker = marker ?? synthetic?.Marker ?? GoroseiMarkerSnapshot.Unknown,
        ObservedObjects = entries.Sum(entry => entry.Count), MappedObjects = entries.Count,
        ForeignObjects = 8, MapState = new MapStateSample(round, 0, "악몽")
    };
}
