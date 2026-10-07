using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OrandOverlay;

public enum AuxiliaryPane { Inventory, Profile, Settings }

public partial class AuxiliaryWindow : Window
{
    private readonly Window _main;
    private FrameworkElement? _paneContent;
    private ScrollViewer? _scroll;
    private bool _placing;
    private bool _closed;
    private HwndSource? _source;
    public AuxiliaryPane Pane { get; private set; }
    internal Rect LastPlacement { get; private set; }
    internal Rect LastWorkArea { get; private set; }

    public AuxiliaryWindow(Window owner)
    {
        InitializeComponent();
        _main = owner; Owner = owner; ShowActivated = owner.ShowActivated;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = 1; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var border = MainPlanTheme.NativeBorderColor;
            var result = DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            if (result != 0) System.Diagnostics.Trace.TraceWarning("Auxiliary border color unavailable: {0}", result);
            _source = HwndSource.FromHwnd(handle); _source?.AddHook(WindowMessage);
            PlaceBesideOwner();
        };
        Loaded += (_, _) => PlaceBesideOwner();
        DpiChanged += PaneDpiChanged;
        owner.LocationChanged += OwnerMoved;
        owner.SizeChanged += OwnerResized;
        owner.DpiChanged += PaneDpiChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            owner.LocationChanged -= OwnerMoved; owner.SizeChanged -= OwnerResized; owner.DpiChanged -= PaneDpiChanged;
            _source?.RemoveHook(WindowMessage);
        };
    }

    internal void SetPane(AuxiliaryPane pane, FrameworkElement content)
    {
        if (_paneContent is not null) throw new InvalidOperationException("Detach the current pane before switching.");
        Pane = pane; _paneContent = content;
        PaneTitle.Text = pane switch { AuxiliaryPane.Inventory => "보유 패", AuxiliaryPane.Profile => "공략 설정", _ => "설정" };
        Title = "랜디픽 · " + PaneTitle.Text;
        AutomationProperties.SetItemStatus(this, PaneTitle.Text);
        if (pane == AuxiliaryPane.Inventory) PaneHost.Content = content;
        else
        {
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = content };
            PaneHost.Content = _scroll;
        }
    }

    internal FrameworkElement? DetachPane()
    {
        if (_scroll is not null) _scroll.Content = null;
        PaneHost.Content = null; _scroll = null;
        var content = _paneContent; _paneContent = null; return content;
    }

    internal void PlaceBesideOwner()
    {
        if (_closed || _placing) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return; // Initial placement happens at SourceInitialized.
        _placing = true;
        try
        {
            var ownerHandle = new WindowInteropHelper(_main).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(ownerHandle, 2), ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            LastWorkArea = info.Work.ToRect();
            LastPlacement = AuxiliaryWindowPlacement.Calculate(ReadBounds(_main), LastWorkArea, GetDpiForWindow(ownerHandle) / 96d);
            if (!SetWindowPos(handle, IntPtr.Zero, (int)LastPlacement.X, (int)LastPlacement.Y,
                    (int)LastPlacement.Width, (int)LastPlacement.Height, 0x0014))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { _placing = false; }
    }

    internal static Rect ReadBounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return rect.ToRect();
    }
    private void OwnerMoved(object? sender, EventArgs e) => PlaceBesideOwner();
    private void OwnerResized(object sender, SizeChangedEventArgs e) => PlaceBesideOwner();
    private void PaneDpiChanged(object sender, DpiChangedEventArgs e) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlaceBesideOwner));
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x001A) Dispatcher.BeginInvoke(new Action(PlaceBesideOwner));
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    { public int Left, Top, Right, Bottom; public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top); }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
