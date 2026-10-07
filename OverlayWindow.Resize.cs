using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace OrandOverlay;

public partial class OverlayWindow
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13;
    private const int HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
    private HwndSource? _resizeSource;

    protected override bool SupportsUserResize => true;
    protected override double MinimumDesignWidth => 420;
    protected override double MinimumDesignHeight => 420;

    private void InitializeResizeBehavior()
    {
        SourceInitialized += (_, _) =>
        {
            _resizeSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _resizeSource?.AddHook(ResizeWindowMessage);
        };
        Closed += (_, _) =>
        {
            _resizeSource?.RemoveHook(ResizeWindowMessage);
            _resizeSource = null;
        };
    }

    private IntPtr ResizeWindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (message != WmNcHitTest || WindowState != System.Windows.WindowState.Normal)
            return IntPtr.Zero;
        if (!GetWindowRect(handle, out var bounds)) return IntPtr.Zero;
        var x = unchecked((short)((long)lParam & 0xffff));
        var y = unchecked((short)(((long)lParam >> 16) & 0xffff));
        var border = Math.Max(5, (int)Math.Ceiling(6 * GetDpiForWindow(handle) / 96d));
        var left = x < bounds.Left + border;
        var right = x >= bounds.Right - border;
        var top = y < bounds.Top + border;
        var bottom = y >= bounds.Bottom - border;
        var hit = top && left ? HtTopLeft : top && right ? HtTopRight :
            bottom && left ? HtBottomLeft : bottom && right ? HtBottomRight :
            left ? HtLeft : right ? HtRight : top ? HtTop : bottom ? HtBottom : 0;
        if (hit == 0) return IntPtr.Zero;
        handled = true;
        return (IntPtr)hit;
    }

    private void ResizeGrip_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, (IntPtr)HtBottomRight, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResizeNativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out ResizeNativeRect bounds);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
