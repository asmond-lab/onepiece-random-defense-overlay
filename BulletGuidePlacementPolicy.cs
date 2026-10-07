using System.Globalization;

namespace OrandOverlay;

/// <summary>
/// Current observations must come from WarcraftCombatReader's version/map-pinned,
/// local-owner-only, stable snapshot path. CoachFrame does not carry a map hash.
/// This is information, not a safe position or a placement suitability verdict.
/// </summary>
public static class BulletGuidePlacementPolicy
{
    public static string? Summary(CoachFrame frame)
    {
        if (frame.Mode != PlayMode.Guide || frame.GuideNumber != 1 || !frame.IsCurrent ||
            frame.Paused || !frame.HasKnownDifficulty || frame.Outcome is "clear" or "fail" ||
            frame.GuidePlan is not { OwnedBullet: true } ||
            frame.Inventory.GetValueOrDefault(BulletGuidePolicy.GoalId) <= 0)
            return null;
        var bullets = frame.CombatObservations.Where(unit =>
            unit.Kind == CombatUnitKind.Bullet && unit.Rawcode == "h081").ToArray();
        if (bullets.Length != 1) return null;
        var bullet = bullets[0];
        if (bullet.Owner > 3 || bullet.Life is not > 0 ||
            !float.IsFinite(bullet.Life.Value) || bullet.Position is not { } point ||
            !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return null;
        // Pinned war3map.j: sw mapping 15634–15637; rects 85577/84/91/98;
        // gVw 46655–46679 explicitly uses GetRectCenterX/Y(sw[slot]).
        var (x, y) = bullet.Owner switch
        {
            0 => (-4960, 5632), 1 => (1056, 5632),
            2 => (-4960, 512), _ => (1056, 512)
        };
        var distance = Math.Sqrt(Math.Pow((double)point.X - x, 2) + Math.Pow((double)point.Y - y, 2));
        return string.Create(CultureInfo.InvariantCulture,
            $"일반 라운드 생성 중심 ({x}, {y}) · 현재 거리 {distance:0.##} · 원문 목표 약 400");
    }
}
