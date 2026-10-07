using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OrandOverlay;

internal sealed class NonActivatingWindowBehavior : IDisposable
{
    private const int ExtendedStyle = -20, NoActivate = 0x08000000, MouseActivate = 0x0021;
    private readonly Window _window;
    private HwndSource? _source;

    private NonActivatingWindowBehavior(Window window)
    {
        _window = window;
        window.ShowActivated = false;
        window.SourceInitialized += SourceInitialized;
        window.Closed += Closed;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply();
    }

    internal static IDisposable Attach(Window window) => new NonActivatingWindowBehavior(window);

    private void SourceInitialized(object? sender, EventArgs e) => Apply();
    private void Closed(object? sender, EventArgs e) => Dispose();

    private void Apply()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessage);
        var style = GetWindowLong(handle, ExtendedStyle);
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLong(handle, ExtendedStyle, style | NoActivate);
        var error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0) throw new Win32Exception(error);
    }

    private static IntPtr WindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != MouseActivate) return IntPtr.Zero;
        handled = true;
        // MA_NOACTIVATE preserves the click; MA_NOACTIVATEANDEAT would swallow controls.
        return (IntPtr)3;
    }

    public void Dispose()
    {
        _window.SourceInitialized -= SourceInitialized;
        _window.Closed -= Closed;
        _source?.RemoveHook(WindowMessage);
        _source = null;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
