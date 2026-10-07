using System.Collections.Immutable;
using System.Text;
namespace OrandOverlay;

public partial class MainWindow
{
    // Behavior-preserving pure seams. ScanCoreAsync rejects old generations before calling CaptureCoachObservation.
    internal static ImmutableArray<CombatUnitState> AcceptCombatObservations(RecognitionResult result) =>
        result.State == RecognitionState.Ready && !result.ConfirmsSessionBoundary ? result.CombatObservations : [];

    internal static void AppendCombatObservationFingerprint(StringBuilder builder, IEnumerable<CombatUnitState> observations)
    {
        foreach (var unit in observations) builder.Append('|').Append(unit);
    }
}
