using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OrandOverlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var checks = new List<object>(); var failures = new List<string>();
        var fixture = OverlayExecutionContext.Fixture(new AppSettings { AutoScanEnabled = false,
            TelemetryEnabled = false, ClearDataAutoRefresh = false, AutoUpdateEnabled = false });
        var app = new App { Execution = fixture, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent(); TelemetryConsentStartup.ClearStartupUri(app);
        var marker = new Window { Title = "SYNTHETIC FOREGROUND TEST", Width = 340, Height = 140,
            Left = 40, Top = 40, Content = new TextBlock { Text = "Owned synthetic foreground window", Margin = new Thickness(12) } };
        var overlay = new OverlayWindow { Left = 400, Top = 40 };
        var stats = overlay.Stats; stats.Left = 1000; stats.Top = 40;
        var craft = new NormalCraftWindow(marker, marker, new Border(), false) { Left = 40, Top = 500 };
        try
        {
            Check(!fixture.RuntimeEnabled && !fixture.LiveMemoryEnabled, "fixture-no-runtime-or-game-reader");
            marker.Show(); Drain();
            foreach (var window in new Window[] { overlay, stats, craft })
            {
                marker.Activate(); Drain(); var foreground = GetForegroundWindow();
                checks.Add(new { Window = window.GetType().Name, MarkerForeground = foreground == new WindowInteropHelper(marker).Handle });
                if (window is NormalCraftWindow c) c.ShowWorkspace(); else window.Show(); Drain();
                var handle = new WindowInteropHelper(window).Handle;
                var style = GetWindowLong(handle, -20);
                var mouse = SendMessage(handle, 0x0021, handle, (0x0201 << 16) | 1);
                checks.Add(new { Window = window.GetType().Name, ShowActivated = window.ShowActivated,
                    ExtendedStyle = style.ToString("X8"), MouseActivateResult = (long)mouse,
                    ForegroundStayed = GetForegroundWindow() == foreground });
                Check(!window.ShowActivated, "show-does-not-activate-" + window.GetType().Name);
                Check((style & 0x08000000) != 0, "native-noactivate-style-" + window.GetType().Name);
                Check(mouse == 3, "mouse-message-preserved-without-activation-" + window.GetType().Name);
                Check(GetForegroundWindow() == foreground, "foreground-preserved-" + window.GetType().Name);
            }
            overlay.SetClickThrough(true); overlay.SetClickThrough(false); Drain();
            Check((GetWindowLong(new WindowInteropHelper(overlay).Handle, -20) & 0x08000000) != 0,
                "noactivate-survives-click-through-toggle");
            var original = new Rect(craft.Left, craft.Top, craft.Width, craft.Height);
            SendMessage(new WindowInteropHelper(craft).Handle, 0x0010, 0, 0); Drain();
            Check(!craft.IsVisible, "native-close-hides-craft");
            craft.SetDisplayAllowed(false); craft.SetDisplayAllowed(true); Drain();
            Check(!craft.IsVisible, "global-toggle-does-not-reopen-user-closed-craft");
            craft.Topmost = false; craft.ShowWorkspace(); Drain();
            craft.SetDisplayAllowed(false); Drain(); Check(!craft.IsVisible, "global-hide-hides-craft");
            craft.SetDisplayAllowed(true); Drain(); Check(craft.IsVisible, "global-show-restores-requested-craft");
            Check(craft.IsVisible && craft.Topmost && original == new Rect(craft.Left, craft.Top, craft.Width, craft.Height),
                "explicit-reopen-preserves-bounds-restores-topmost");
            var close = Visuals(craft).OfType<Button>().SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == "normal-close-craft-window");
            Check(close is not null && close.IsVisible && close.IsEnabled, "discoverable-close-button");
            if (close is not null) { close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(); Check(!craft.IsVisible, "button-close-hides-craft"); }
            craft.ShowWorkspace(); marker.Close(); Drain();
            Check(!Application.Current.Windows.Cast<Window>().Contains(craft), "host-close-destroys-craft");
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            craft.CloseForApplication(); overlay.CloseForApplication(); stats.CloseForApplication(); marker.Close();
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { SyntheticOnly = true,
                NativeMessagesTargetOwnedWindowsOnly = true, GameInput = false, Checks = checks, Failures = failures }, new JsonSerializerOptions { WriteIndented = true }));
            app.Shutdown();
        }
        Console.WriteLine($"OVERLAY INPUT {(failures.Count == 0 ? "PASS" : "FAIL")}: {failures.Count} failures");
        foreach (var failure in failures) Console.WriteLine(failure);
        return failures.Count == 0 ? 0 : 1;
        void Check(bool passed, string name) { checks.Add(new { Name = name, Passed = passed }); if (!passed) failures.Add(name); }
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
