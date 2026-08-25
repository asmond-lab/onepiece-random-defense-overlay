namespace OrandOverlay;

public static class OverlayLayoutPolicy
{
    // 후보 보드는 자체 ScrollViewer가 오버플로를 소유한다. 양수 최소 높이를 강제하면
    // 상단 안내가 두 줄 이상일 때 고정 높이 창 밖으로 하단 상태줄이 밀려난다.
    public const double ScrollableBoardMinimumHeight = 0;

    public static StatsOverlayLayout StatsLayout(OverlayDisplayMode mode) =>
        mode == OverlayDisplayMode.StatsOnlyCompact
            ? new StatsOverlayLayout(228, 360, false)
            : new StatsOverlayLayout(228, 700, true);
}

public sealed record StatsOverlayLayout(
    double Width,
    double Height,
    bool NonCoreVisible);
