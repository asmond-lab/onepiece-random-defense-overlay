using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OrandOverlay;

public sealed partial class NormalCraftWindow
{
    private readonly CraftWindowGeometryStore? _geometryStore;
    private readonly DispatcherTimer _geometrySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private HwndSource? _geometrySource;
    private DispatcherOperation? _geometryOperation;
    private bool _geometryReady, _applyingGeometry, _geometryDirty, _nativeSizeMove;

    private void InitializeGeometry(Window referenceOverlay)
    {
        if (_geometryStore is null) return; // Synthetic rendering never reads or writes user settings.
        var handle = new WindowInteropHelper(this).Handle;
        _geometrySource = HwndSource.FromHwnd(handle);
        _geometrySource?.AddHook(GeometryMessage);
        _geometrySaveTimer.Tick += SaveGeometryTick;
        LocationChanged += GeometryChanged;
        SizeChanged += GeometrySizeChanged;
        // Loaded is one-shot: native size/layout and stats position restoration must have finished.
        // A later Show or same-hand render must not re-anchor a user-positioned window.
        var saved = _geometryStore.Load();
        RoutedEventHandler? firstLoaded = null;
        firstLoaded = (_, _) =>
        {
            Loaded -= firstLoaded;
            var referenceHandle = new WindowInteropHelper(referenceOverlay).EnsureHandle();
            var anchor = AuxiliaryWindow.ReadBounds(referenceOverlay);
            var monitor = saved is null ? MonitorFromWindow(referenceHandle, 2)
                : MonitorFor(saved);
            var workArea = MonitorWorkArea(monitor);
            var scale = MonitorScale(monitor, handle);
            var placement = saved is null
                ? CraftWindowPlacement.Calculate(anchor, workArea, new(PreferredWidth, PreferredHeight), scale)
                : CraftWindowPlacement.Restore(saved, workArea, scale);
            ApplyGeometry(placement, workArea, scale);
            var actualScale = CraftWindowPlacement.Scale(GetDpiForWindow(handle) / 96d);
            if (Math.Abs(actualScale - scale) > 0.001)
            {
                // HWND DPI after a cross-monitor move is authoritative under every awareness mode.
                placement = saved is null
                    ? CraftWindowPlacement.Calculate(anchor, workArea, new(PreferredWidth, PreferredHeight), actualScale)
                    : CraftWindowPlacement.Restore(saved, workArea, actualScale);
                ApplyGeometry(placement, workArea, actualScale);
            }
            _geometryReady = true;
        };
        // WPF can raise Loaded before SourceInitialized (for example an already-laid-out
        // transparent window). In that order, subscribing to Loaded alone loses placement
        // and leaves persistence permanently unready. Queue once after native creation.
        if (IsLoaded)
            _geometryOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _geometryOperation = null;
                if (!_closed) firstLoaded(this, new RoutedEventArgs());
            }));
        else Loaded += firstLoaded;
    }

    private void ApplyGeometry(Rect bounds, Rect workArea, double scale)
    {
        if (_closed) return;
        _applyingGeometry = true;
        try
        {
            // Very small work areas override the usual minimum so the whole window remains reachable.
            MinWidth = Math.Min(MinimumWidth, workArea.Width / scale);
            MinHeight = Math.Min(MinimumHeight, workArea.Height / scale);
            var handle = new WindowInteropHelper(this).Handle;
            SetNativeBounds(handle, bounds);
            // Moving across monitors may synchronously dispatch WM_DPICHANGED. Apply the intended
            // physical rectangle once more after WPF has adopted that monitor's DPI.
            SetNativeBounds(handle, bounds);
        }
        finally { _applyingGeometry = false; }
    }

    private static void SetNativeBounds(IntPtr handle, Rect bounds)
    {
        if (!SetWindowPos(handle, IntPtr.Zero, (int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
            (int)Math.Floor(bounds.Width), (int)Math.Floor(bounds.Height), 0x0014))
            System.Diagnostics.Trace.TraceWarning("Craft window geometry was not applied: {0}", Marshal.GetLastWin32Error());
    }

    private void GeometryChanged(object? sender, EventArgs e) => ScheduleGeometrySave();
    private void GeometrySizeChanged(object sender, SizeChangedEventArgs e) => ScheduleGeometrySave();
    private void ScheduleGeometrySave()
    {
        if (!_geometryReady || _applyingGeometry || _closed || !IsVisible || WindowState != WindowState.Normal) return;
        _geometryDirty = true;
        _geometrySaveTimer.Stop();
        if (!_nativeSizeMove) _geometrySaveTimer.Start();
    }

    private void SaveGeometryTick(object? sender, EventArgs e) => FlushGeometry();
    private void FlushGeometry()
    {
        _geometrySaveTimer.Stop();
        if (!_geometryReady || !_geometryDirty || _geometryStore is null || _applyingGeometry || _closed) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || WindowState != WindowState.Normal) return;
        var bounds = AuxiliaryWindow.ReadBounds(this);
        var scale = CraftWindowPlacement.Scale(GetDpiForWindow(handle) / 96d);
        var geometry = new CraftWindowGeometry(bounds.Left, bounds.Top, bounds.Width / scale, bounds.Height / scale)
            { DpiScale = scale };
        if (_geometryStore.Save(geometry)) _geometryDirty = false;
    }

    private IntPtr GeometryMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0231) { _nativeSizeMove = true; _geometrySaveTimer.Stop(); } // WM_ENTERSIZEMOVE
        if (message == 0x0232) // WM_EXITSIZEMOVE: save actual native bounds, never move back to the anchor.
        {
            _nativeSizeMove = false;
            ScheduleGeometrySave(); FlushGeometry();
        }
        if (_geometryReady && !_applyingGeometry && (message == 0x007E || message == 0x001A))
        {
            // Only display/work-area topology changes clamp an already-positioned window.
            _geometryOperation?.Abort();
            _geometryOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampAfterDisplayChange));
        }
        return IntPtr.Zero;
    }

    private void ClampAfterDisplayChange()
    {
        _geometryOperation = null;
        if (_closed || _nativeSizeMove || WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var bounds = AuxiliaryWindow.ReadBounds(this);
        var monitor = MonitorFromWindow(handle, 2);
        var workArea = MonitorWorkArea(monitor);
        var scale = CraftWindowPlacement.Scale(GetDpiForWindow(handle) / 96d);
        var current = new CraftWindowGeometry(bounds.Left, bounds.Top, bounds.Width / scale, bounds.Height / scale);
        ApplyGeometry(CraftWindowPlacement.Restore(current, workArea, scale), workArea, scale);
        _geometryDirty = true; FlushGeometry();
    }

    private void DisposeGeometry()
    {
        _geometrySaveTimer.Stop();
        _geometrySaveTimer.Tick -= SaveGeometryTick;
        LocationChanged -= GeometryChanged;
        SizeChanged -= GeometrySizeChanged;
        _geometryOperation?.Abort(); _geometryOperation = null;
        _geometrySource?.RemoveHook(GeometryMessage); _geometrySource = null;
    }

    private static IntPtr MonitorFor(CraftWindowGeometry saved)
    {
        var bounds = new NativeRect { Left = (int)saved.LeftPixels, Top = (int)saved.TopPixels,
            Right = (int)(saved.LeftPixels + saved.WidthDip * saved.DpiScale),
            Bottom = (int)(saved.TopPixels + saved.HeightDip * saved.DpiScale) };
        return MonitorFromRect(ref bounds, 2); // MONITOR_DEFAULTTONEAREST also covers removed monitors.
    }

    private static Rect MonitorWorkArea(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    private static double MonitorScale(IntPtr monitor, IntPtr window)
    {
        // GetDpiForMonitor follows the process DPI awareness, as do these native desktop coordinates.
        if (GetDpiForMonitor(monitor, 0, out var x, out _) == 0 && x > 0) return x / 96d;
        return CraftWindowPlacement.Scale(GetDpiForWindow(window) / 96d);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect bounds, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
