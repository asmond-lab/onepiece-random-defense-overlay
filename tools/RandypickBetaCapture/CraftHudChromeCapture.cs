using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OrandOverlay;

internal static partial class DiagnosticCapture
{
    private static void ValidateCraftHudChrome(NormalCraftWindow window, Action<bool, string> check)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var bounds = AuxiliaryWindow.ReadBounds(window);
        nint Hit(double x, double y)
        {
            var packed = unchecked(((int)(short)y << 16) | (ushort)(short)x);
            return SendCraftChromeMessage(handle, 0x0084, 0, packed);
        }
        check(Hit(bounds.Left + bounds.Width / 2, bounds.Top + 16) == 2, "detached-native-header-hit-is-drag-caption");
        check(Hit(bounds.Left + 1, bounds.Top + bounds.Height / 2) == 10, "detached-native-left-edge-is-resizable");
        check(Hit(bounds.Right - 1, bounds.Top + bounds.Height / 2) == 11, "detached-native-right-edge-is-resizable");
        check(Hit(bounds.Right - 2, bounds.Bottom - 2) == 17, "detached-native-bottom-right-corner-is-resizable");
        check(SendCraftChromeMessage(handle, 0x0021, handle, 0) == 3, "detached-native-mouse-activate-is-noactivate");
        check((GetCraftExtendedStyle(handle, -20) & 0x08000000) != 0, "detached-native-ws-ex-noactivate-is-set");
        check(window.AllowsTransparency && window.Background == System.Windows.Media.Brushes.Transparent,
            "detached-native-window-allows-real-transparent-corners");
        check(System.Windows.Shell.WindowChrome.GetWindowChrome(window)?.CornerRadius == new CornerRadius(OverlayTheme.ChromeRadius),
            "detached-native-window-chrome-uses-shared-radius");
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendCraftChromeMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetCraftExtendedStyle(nint window, int index);
}
