using System.Collections.Immutable;
using OrandOverlay;

namespace PlannerEvidenceCapture;

// Pure projection of the same decision/frame consumed by the capture surface.
// Object-list serialization retains the concrete event properties alongside the standard fields.
internal static class BulletGuideRowProjection
{
    internal static bool ConfirmsSessionBoundary(string name) =>
        name is "new-session" or "overnight-new-session" or
            "nasjuro-reset-warcury" or "nasjuro-reset-nasjuro" or "nasjuro-reset-none" or
            "queen-event-reset" or "stop-event-reset";

    // Correlate the requested input, never poll for a desired recommendation.
    internal static bool MatchesObservation(CoachFrame frame, int round, long generation, long revision,
        IReadOnlyDictionary<string, int> inventory, GoroseiMarkerSnapshot marker) =>
        frame.Round == round && frame.MatchGeneration == generation && frame.RecognitionRevision == revision &&
        frame.Inventory.Count == inventory.Count && inventory.All(p => frame.Inventory.TryGetValue(p.Key, out var count) && count == p.Value) &&
        Equals(frame.Gorosei.Marker, marker);

    public static BulletGuideRow Observe(string name, CoachDecision decision, CoachFrame frame) =>
        new(name, decision, frame);

    public static StopZeroRow StopZero(CoachDecision decision, CoachFrame frame) => new(decision, frame);
    public static StopRetainedRow StopRetained(CoachDecision decision, CoachFrame frame) => new(decision, frame);
    public static QueenRow Queen(int index, CoachDecision decision, CoachFrame frame, QueenConversionInput input) =>
        new(index, decision, frame, input);
}

internal class BulletGuideRow(string name, CoachDecision decision, CoachFrame frame)
{
    public string Name { get; } = name;
    public CoachDecision Decision { get; } = decision;
    public string? GoalId { get; } = frame.GoalId;
    public int GuideNumber { get; } = frame.GuideNumber;
    public BulletGuidePlan? GuidePlan { get; } = frame.GuidePlan;
    public string? ConfirmedNavigation { get; } = frame.ConfirmedNavigation;
    public string Difficulty { get; } = frame.Difficulty;
    public GoroseiObservation Gorosei { get; } = frame.Gorosei;
    public GoroseiPlanningSelection PlanningGorosei { get; } = frame.PlanningGorosei;
    public GoroseiMode CurrentGoroseiEffect { get; } = frame.CurrentGoroseiEffect;
    public long MatchGeneration { get; } = frame.MatchGeneration;
    public long RecognitionRevision { get; } = frame.RecognitionRevision;
    public ShipReservations? ShipReservations { get; } = frame.ShipReservations;
    public string[] Candidates { get; } = frame.Recommendations.Select(item => item.Route.GoalUnitId).ToArray();
    public string[] Steps { get; } = frame.CraftSteps.Select(step => step.TargetUnitId).ToArray();
}

internal sealed class StopZeroRow(CoachDecision decision, CoachFrame frame)
    : BulletGuideRow("stop-common-transition-manual-zero", decision, frame)
{
    public string Kind => "guard-preserved-common-stop-observation-transition";
    public bool ProductionUncheckedBodyExecuted => false;
    public bool IsCurrent { get; } = frame.IsCurrent;
    public ImmutableArray<CombatUnitState> CombatObservations { get; } = frame.CombatObservations;
    public NativeNavigationSnapshot NativeNavigation { get; } = frame.NativeNavigation;
}

internal sealed class StopRetainedRow(CoachDecision decision, CoachFrame frame)
    : BulletGuideRow("stop-common-transition-manual-retained", decision, frame)
{
    public string Kind => "guard-preserved-common-stop-observation-transition";
    public ImmutableDictionary<string, int> Inventory { get; } = frame.Inventory;
    public bool IsCurrent { get; } = frame.IsCurrent;
}

internal sealed class QueenRow(int index, CoachDecision decision, CoachFrame frame, QueenConversionInput input)
    : BulletGuideRow($"queen-event-surface-{index}", decision, frame)
{
    public string Provenance => "user-selection-not-native";
    public long Revision { get; } = frame.Revision;
    public QueenConversionInput Input { get; } = input;
    public bool StaleTokensRejected => true;
}
