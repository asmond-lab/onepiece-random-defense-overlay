namespace OrandOverlay;

public enum BlackMariaMode { Stun, Slow, Burn }

/// <summary>Consumes only the current reader-owned snapshot, cleared on disconnect/session boundary.</summary>
public static class BlackMariaObservation
{
    public static BlackMariaMode? Selected(CoachFrame frame)
    {
        if (!frame.IsCurrent || frame.MatchGeneration <= 0 || frame.Revision <= 0 ||
            frame.Inventory.GetValueOrDefault(BulletGuideBlackMariaPolicy.UnitId) != 1) return null;
        var units = frame.CombatObservations.Where(unit => unit.Rawcode == "h04U").ToArray();
        if (units.Length != 1) return null;
        var unit = units[0];
        return unit.Kind == CombatUnitKind.LocalUnit && unit.Owner <= 3 && unit.Life is > 0 &&
            float.IsFinite(unit.Life.Value) ? unit.BlackMariaSelectedMode : null;
    }

    public static string Label(BlackMariaMode? mode) => mode switch
    {
        BlackMariaMode.Stun => "스턴", BlackMariaMode.Slow => "이감", BlackMariaMode.Burn => "화상", _ => "미확인"
    };
}
