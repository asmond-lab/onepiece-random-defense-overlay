using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using OrandOverlay;

internal static partial class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new App { Execution = OverlayExecutionContext.Fixture(new AppSettings()) };
        app.InitializeComponent(); TelemetryConsentStartup.ClearStartupUri(app);
        var main = new MainWindow(OverlayExecutionContext.Fixture(new AppSettings {
            Mode = PlayMode.Guide, GuideNumber = 1, AutoScanEnabled = true, TelemetryEnabled = false, ClearDataAutoRefresh = false }));
        try
        {
            main.Show();
            main.Left = 420;
            main.Top = 40;
            Click(main, "main-inventory-navigation");
            var auxiliary = Pane(main);
            Check(auxiliary is not null, "RED: inventory navigation must open an owned auxiliary window, not the inline settings drawer");
            Check(main.IsVisible && main.IsEnabled, "Main plan was hidden or made modal");
            VerifyPane(main, auxiliary!, "Inventory", ["InventoryList"], ["PlayModeCombo", "AutoScanCheck"]);
            Save(auxiliary!, "inventory");
            var list = (ListBox)main.FindName("InventoryList");
            list.Items.Add("검증용 관측 패  ×2");
            auxiliary!.UpdateLayout();
            Check(list.Items.Contains("검증용 관측 패  ×2") && Window.GetWindow(list) == auxiliary,
                "Live list instance was replaced with a disconnected copy");
            list.Items.Remove("검증용 관측 패  ×2");
            Click(main, "main-profile-navigation");
            Check(ReferenceEquals(auxiliary, Pane(main)), "Switching created a duplicate window");
            VerifyPane(main, auxiliary!, "Profile", ["PlayModeCombo", "ManualGoalsPanel"], ["InventoryList", "AutoScanCheck"]);
            var guide = (ComboBox)((BeginnerCoachView)main.FindName("MainCoachView")).FindName("GuideChoice");
            auxiliary!.UpdateLayout();
            Check(Window.GetWindow(guide) == auxiliary && guide.IsVisible,
                "Guide selection must be available before the first game");
            Save(auxiliary!, "profile");
            Click(main, "main-settings-navigation");
            VerifyPane(main, auxiliary!, "Settings", ["AutoScanCheck", "HotkeyBox"], ["InventoryList", "PlayModeCombo"]);
            Save(auxiliary!, "settings");
            Click(main, "main-settings-navigation");
            Check(Pane(main) is null, "Repeated active navigation must close the panel");
            Click(main, "main-inventory-navigation");
            Check(Pane(main) is { IsVisible: true }, "Panel failed to reopen");
            main.Left = 0;
            Pane(main)!.UpdateLayout();
            Check(Pane(main)!.Left >= SystemParameters.WorkArea.Left, "Panel was placed offscreen at left edge");
            Save(Pane(main)!, "inventory-edge");
            main.Close();
            Check(!auxiliary.IsVisible && main.OwnedWindows.Count == 0, "Closing main retained auxiliary window");
            Console.WriteLine("LEFT_PANELS route contract PASS");
        }
        finally { main.Close(); app.Shutdown(); }
    }
    private static void Save(Window window, string name)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(Environment.CurrentDirectory, name + ".png"));
        encoder.Save(stream);
    }
    private static Window? Pane(MainWindow main) => main.OwnedWindows.Cast<Window>().SingleOrDefault(window =>
        AutomationProperties.GetAutomationId(window) == "main-auxiliary-window");
    private static void Click(MainWindow main, string id) => Descendants(main).OfType<Button>().Single(button =>
        AutomationProperties.GetAutomationId(button) == id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void VerifyPane(MainWindow main, Window pane, string kind, string[] present, string[] absent)
    {
        pane.UpdateLayout();
        Check(AutomationProperties.GetItemStatus(pane) == kind, "Navigation routed to the wrong pane");
        foreach (var name in present) Check(Window.GetWindow((DependencyObject)main.FindName(name)) == pane, "Required control missing: " + name);
        foreach (var name in absent) Check(Window.GetWindow((DependencyObject)main.FindName(name)) != pane, "Unrelated control leaked into pane: " + name);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
