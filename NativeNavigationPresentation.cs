namespace OrandOverlay;

public static class NativeNavigationPresentation
{
    public static string Describe(NativeNavigationSnapshot native, string? userConfirmed)
    {
        if (native.Status == NativeNavigationStatus.Selected && native.OptionId is { } id)
            return $"게임 항법 · {NavigationProfiles.Find(id).Name} (게임에서 확인)";
        if (native.Status == NativeNavigationStatus.Unselected)
            return "항법을 아직 선택하지 않았습니다.";
        if (native.Status == NativeNavigationStatus.Conflict)
            return "게임에서 선택한 항법을 확인하지 못했습니다. 확인될 때까지 재료를 사용하지 마세요.";
        return userConfirmed is { } manual
            ? $"직접 선택했다고 알려준 항법 · {NavigationProfiles.Find(manual).Name}. 게임에서 선택됐는지는 아직 확인하지 못했습니다."
            : "게임에서 선택한 항법을 확인해 주세요.";
    }
}
