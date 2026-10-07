namespace OrandOverlay;

public static class NavigationCarryPolicy
{
    public static TopScope PreferredScope(GoalCarryMode mode) =>
        mode == GoalCarryMode.MultiRequired
            ? TopScope.MultiTop
            : TopScope.SoloTop;

    public static bool Fits(GoalCarryMode mode, NavigationOption option)
    {
        var scope = PreferredScope(mode);
        return scope == TopScope.MultiTop
            ? option.AllowsMultipleTopUnits
            : option.CanCraftTopUnits && !option.AllowsMultipleTopUnits;
    }
}
