using System.Windows.Input;

namespace OrandOverlay;

internal static class OverlayHotkeyPolicy
{
    internal const Key DefaultKey = Key.F1;
    internal const uint RegistrationModifiers = 0x4000;

    internal static bool Normalize(AppSettings settings)
    {
        var key = settings.OverlayToggleKey;
        var legacy = !settings.OverlayToggleKeyCustomized &&
            (string.Equals(key, "Capital", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "CapsLock", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(key, "Scroll", StringComparison.OrdinalIgnoreCase));
        if (!legacy && IsUsable(key)) return false;
        settings.OverlayToggleKey = DefaultKey.ToString();
        return true;
    }

    private static bool IsUsable(string? name) => Enum.TryParse<Key>(name, true, out var key) && Enum.IsDefined(key) &&
        key is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin);

    internal static Key ResolveKey(string keyName) => IsUsable(keyName) ? Enum.Parse<Key>(keyName, true) : DefaultKey;
    internal static bool ShouldToggle(bool registered, bool capturing) => registered && !capturing;
}
