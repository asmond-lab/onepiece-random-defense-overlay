using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace OrandOverlay;

public sealed partial class NormalCraftWindow : Window
{
    private readonly Window _host;
    internal Window HostWindow => _host;
    private bool _allowClose;
    private bool _closed;
    private bool _requestedVisible;
    private bool _displayAllowed = true;
    internal const double PreferredWidth = 326, PreferredHeight = 440;
    internal const double MinimumWidth = 320, MinimumHeight = 260;

    internal NormalCraftWindow(Window owner, Window referenceOverlay, FrameworkElement workspace, bool runtimeEffects,
        string? geometrySettingsPath = null)
    {
        _geometryStore = CraftWindowGeometryStore.Create(runtimeEffects, geometrySettingsPath);
        _host = owner; Title = "랜디픽 · 조합 흐름";
        Width = PreferredWidth; MinWidth = MinimumWidth;
        Height = PreferredHeight; MinHeight = MinimumHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 36, ResizeBorderThickness = new(5),
            GlassFrameThickness = new(0), CornerRadius = new(OverlayTheme.ChromeRadius), UseAeroCaptionButtons = false });
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        FontFamily = new FontFamily("Malgun Gothic");
        Background = Brushes.Transparent; Foreground = OverlayTheme.PlanText;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Left = double.IsFinite(owner.Left) ? owner.Left + 40 : 40;
        Top = double.IsFinite(owner.Top) ? owner.Top + 40 : 40;
        AutomationProperties.SetAutomationId(this, "normal-craft-window");
        OverlayChrome.EnsureOpaqueDefaults();
        OverlayChrome.ApplyTranslucent(Resources);
        void ApplyFocusGate() => Opacity = ForegroundGameWatcher.IsGameForeground ? 1 : 0;
        ForegroundGameWatcher.Changed += ApplyFocusGate;
        Closed += (_, _) => ForegroundGameWatcher.Changed -= ApplyFocusGate;
        ApplyFocusGate();
        var root = new Grid().WithOverlayBackground(OverlayChrome.ShellKey);
        OverlayTheme.AttachRoundClip(root, OverlayTheme.ChromeRadius);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        var title = new TextBlock { Text = "조합식", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new(10, 0, 64, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = OverlayTheme.PlanText, IsHitTestVisible = false };
        root.Children.Add(title);
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);
        var close = new Button { Content = "닫기", Width = 52, Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(0, 4, 6, 0), Padding = new(8, 0, 8, 0), FontSize = 12,
            Foreground = OverlayTheme.PlanText,
            BorderBrush = OverlayTheme.PlanLine, BorderThickness = new(1),
            ToolTip = "조합창 닫기 · 유닛 선택 또는 조합창 열기로 다시 표시" };
        close.WithOverlayBackground(OverlayChrome.WellKey);
        AutomationProperties.SetAutomationId(close, "normal-close-craft-window");
        AutomationProperties.SetName(close, "조합창 닫기");
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        var grip = new ResizeGrip { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Foreground = OverlayTheme.PlanText,
            ToolTip = "드래그하여 조합창 크기 조절" };
        AutomationProperties.SetAutomationId(grip, "normal-craft-resize-grip");
        AutomationProperties.SetName(grip, "조합창 크기 조절");
        WindowChrome.SetResizeGripDirection(grip, ResizeGripDirection.BottomRight);
        Grid.SetRow(grip, 2); root.Children.Add(grip);
        var shellOutline = new Border { BorderBrush = OverlayTheme.PlanLine, BorderThickness = new(1),
            CornerRadius = new(OverlayTheme.ChromeRadius), IsHitTestVisible = false };
        Grid.SetRowSpan(shellOutline, 3); root.Children.Add(shellOutline);
        Content = root;
        NonActivatingWindowBehavior.Attach(this);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = 1; DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            var border = MainPlanTheme.NativeBorderColor;
            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            InitializeGeometry(referenceOverlay);
        };
        Closing += HideOnClose;
        owner.Closed += HostClosed;
        Closed += (_, _) => { _closed = true; owner.Closed -= HostClosed; DisposeGeometry(); };
    }

    private void HostClosed(object? sender, EventArgs e) => CloseForApplication();

    internal void ShowWorkspace()
    {
        if (_closed) return;
        _requestedVisible = true;
        ApplyVisibility();
    }

    internal void SetDisplayAllowed(bool allowed)
    {
        _displayAllowed = allowed;
        ApplyVisibility();
    }

    internal void ResetWorkspaceVisibility()
    {
        _requestedVisible = false;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (_closed) return;
        if (_requestedVisible && _displayAllowed)
        {
            Topmost = true;
            if (!IsVisible) Show();
        }
        else if (IsVisible) { FlushGeometry(); Hide(); }
    }

    internal void CloseForApplication()
    {
        if (_closed) return;
        FlushGeometry();
        _allowClose = true; Close();
    }

    private void HideOnClose(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        ResetWorkspaceVisibility();
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
