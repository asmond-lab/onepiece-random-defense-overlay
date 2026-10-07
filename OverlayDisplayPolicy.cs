namespace OrandOverlay;

public sealed record OverlayDisplayState(
    OverlayDisplayMode Mode,
    OverlayDisplayMode LastVisibleMode,
    bool Available);

public sealed record OverlayWindowVisibility(
    bool RecommendationVisible,
    bool StatsVisible);

public static class OverlayDisplayPolicy
{
    public static OverlayWindowVisibility Visibility(
        OverlayDisplayState state)
    {
        if (!state.Available || state.Mode == OverlayDisplayMode.Hidden)
            return new OverlayWindowVisibility(false, false);
        return state.Mode == OverlayDisplayMode.StatsOnly
            ? new OverlayWindowVisibility(false, true)
            : new OverlayWindowVisibility(true, true);
    }

    public static OverlayDisplayState Select(
        OverlayDisplayState state, OverlayDisplayMode mode) =>
        mode == OverlayDisplayMode.Hidden
            ? state with { Mode = OverlayDisplayMode.Hidden }
            : state with { Mode = mode, LastVisibleMode = mode };

    public static OverlayDisplayState Toggle(OverlayDisplayState state) =>
        state.Mode == OverlayDisplayMode.Hidden
            ? state with
            {
                Mode = state.LastVisibleMode == OverlayDisplayMode.Hidden
                    ? OverlayDisplayMode.Full
                    : state.LastVisibleMode
            }
            : state with
            {
                LastVisibleMode = state.Mode,
                Mode = OverlayDisplayMode.Hidden
            };

    public static OverlayDisplayState WithAvailability(
        OverlayDisplayState state, bool available) =>
        state with { Available = available };
}
