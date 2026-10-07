using System.IO;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrandOverlay;

internal static class Program
{
    private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int GwlExStyle = -20, WsExTransparent = 0x20, WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084, WmMouseActivate = 0x0021;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        var secondary = System.Windows.Forms.Screen.AllScreens.Where(screen => !screen.Primary)
            .OrderByDescending(screen => (long)screen.WorkingArea.Width * screen.WorkingArea.Height).FirstOrDefault();
        if (secondary is null)
        {
            File.WriteAllText(Path.Combine(output, "placement.json"), "No secondary display connected; GUI not started.");
            return 3;
        }
        var work = secondary.WorkingArea;
        var monitor = MonitorFromPoint(new PointNative(work.Left + work.Width / 2, work.Top + work.Height / 2), 2);
        if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) != 0)
            throw new InvalidOperationException("Secondary monitor DPI unavailable.");
        var scaleX = dpiX / 96d; var scaleY = dpiY / 96d;
        File.WriteAllText(Path.Combine(output, "placement.json"), JsonSerializer.Serialize(new
        {
            selected = secondary.DeviceName, secondary.Primary, work, dpiX, dpiY
        }, new JsonSerializerOptions { WriteIndented = true }));
        var app = new App();
        typeof(App).GetProperty("SkipRuntimeStartup", NonPublicInstance)!.SetValue(app, true);
        app.InitializeComponent();
        typeof(Application).GetField("_startupUri", NonPublicInstance)!.SetValue(app, null);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var exit = 1;
        var window = new OverlayWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = (work.Left + 80) / scaleX, Top = (work.Top + 80) / scaleY
        };
        window.Stats.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Stats.Left = (work.Left + 680) / scaleX;
        window.Stats.Top = (work.Top + 80) / scaleY;
        var fixture = ConfigureUnitCheck(window);
        window.ContentRendered += async (_, _) =>
        {
            try { exit = await RunAsync(window, fixture, output, work); }
            catch (Exception error)
            {
                await File.WriteAllTextAsync(Path.Combine(output, "error.txt"), error.ToString());
                exit = 1;
            }
            finally
            {
                window.Stats.CloseForApplication();
                window.CloseForApplication();
                app.Shutdown(exit);
            }
        };
        app.Run(window);
        return exit;
    }

    private static UnitCheckFixture ConfigureUnitCheck(OverlayWindow window)
    {
        var catalog = new DataCatalog();
        catalog.Load(loadCarryPolicy: false, mapVersion: "2.321");
        var model = NormalCandidateBrowser.Create(catalog);
        var target = catalog.AllUnits
            .Where(unit => NormalCandidateBrowser.Tier(unit) is "희귀함" or "희귀" && unit.Recipe.Count > 0)
            .OrderByDescending(unit => unit.Name.Length)
            .First();
        var inventory = target.Recipe
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        model.Update(inventory, current: true, generation: 321);
        window.NormalView.SetCraftPlanner(new NormalCraftPlanner(catalog));
        window.NormalView.SetModel(model);
        model.PresentationChanged += window.NormalView.Render;
        typeof(OverlayWindow).GetMethod("EnableDetachedCraftLayout", NonPublicInstance)!.Invoke(window, null);
        typeof(OverlayWindow).GetMethod("SetNormalBrowserActive", NonPublicInstance)!.Invoke(window,
            [true, PlayMode.Normal, LongStatus]);
        window.UpdateStatus(LongStatus);
        return new(catalog, model, inventory);
    }

    private const string LongStatus =
        "2.321 유닛 확인 · 긴 한국어 후보 이름과 부족 재료를 비교하고 스크롤하여 다음 조합 대상을 확인하세요.";

    private static async Task<int> RunAsync(OverlayWindow window, UnitCheckFixture fixture, string output, Rectangle work)
    {
        await RenderedAsync(window);
        var handle = new WindowInteropHelper(window).Handle;
        var element = AutomationElement.FromHandle(handle);
        var transform = element.TryGetCurrentPattern(TransformPattern.Pattern, out var raw)
            ? (TransformPattern)raw : null;
        var initial = SizeOf(window);
        var defaultSurface = await ValidateSurfaceAsync(window);
        Save(window, Path.Combine(output, "default.png"));

        var hitTests = HitTests(handle, window);
        var smallRequested = new Size(window.MinWidth, window.MinHeight);
        var small = transform is null ? SizeOf(window) : await ResizeAsync(window, transform, smallRequested);
        var smallSurface = await ValidateSurfaceAsync(window, exerciseScroll: true);
        Save(window, Path.Combine(output, "small.png"));

        var defaultAgain = transform is null ? SizeOf(window) : await ResizeAsync(window, transform, initial);
        var largeRequested = new Size(Math.Max(initial.Width + 160, window.MinWidth + 160),
            Math.Max(initial.Height + 160, window.MinHeight + 160));
        var large = transform is null ? SizeOf(window) : await ResizeAsync(window, transform, largeRequested);
        var largeSurface = await ValidateSurfaceAsync(window);
        Save(window, Path.Combine(output, "large.png"));
        var displayClamp = await ExerciseDisplayClampAsync(window, work);
        if (transform is not null) await ResizeAsync(window, transform, largeRequested);

        var beforeRefresh = SizeOf(window);
        fixture.Model.Update(fixture.Inventory, current: true, generation: 321);
        window.NormalView.Render();
        window.UpdateStatus(LongStatus + " 같은 패 갱신");
        await RenderedAsync(window);
        var afterRefresh = SizeOf(window);

        var beforeMove = new Point(window.Left, window.Top);
        var afterMove = beforeMove;
        if (transform is { Current.CanMove: true })
            afterMove = await MoveAsync(window, transform, new(beforeMove.X + 36, beforeMove.Y + 28));
        var afterNativeMoveSize = SizeOf(window);

        typeof(OverlayWindowBase).GetMethod("ApplyResolutionScale", NonPublicInstance)!.Invoke(window, null);
        await RenderedAsync(window);
        var afterScaleReapply = SizeOf(window);
        window.Hide();
        window.Show();
        await RenderedAsync(window);
        var afterShow = SizeOf(window);

        var style = GetWindowLong(handle, GwlExStyle);
        var foregroundBefore = GetForegroundWindow();
        var mouseActivateResult = (int)SendMessage(handle, WmMouseActivate, handle, (IntPtr)0x02010001);
        var foregroundAfter = GetForegroundWindow();
        window.SetClickThrough(true);
        var clickThroughStyle = GetWindowLong(handle, GwlExStyle);
        window.SetClickThrough(false);
        var restoredStyle = GetWindowLong(handle, GwlExStyle);
        var grip = Find<ResizeGrip>((DependencyObject)window.Content)
            .FirstOrDefault(item => AutomationProperties.GetAutomationId(item) == "overlay-resize-grip");

        var tinyWorkArea = new TinyWorkAreaAssessment(300, 260, window.MinWidth, window.MinHeight,
            window.MinWidth <= 300 && window.MinHeight <= 260,
            "A 300x260 DIP synthetic work area is smaller than the readable minimum; no such desktop was present, so product geometry was not changed without a native reproduction.");
        var surfacePass = new[] { smallSurface, defaultSurface, largeSurface }.All(surface =>
            surface.ModeTabCount == 4 && surface.HeaderVisible && surface.StagePickerVisible &&
            surface.CategoryControlVisible && surface.CandidateCount > 0 && surface.FooterVisible &&
            surface.CandidateScrollExtent > 0 && surface.CandidateScrollViewport > 0);
        var adaptiveColumns = smallSurface.Columns.Length > 0 && defaultSurface.Columns.Length > 0 &&
            largeSurface.Columns.Length > 0 && smallSurface.Columns.Max() < largeSurface.Columns.Max();
        var ownedRect = BoundsOf(handle);
        var onSecondary = work.Contains(ownedRect);
        var report = new
        {
            success = onSecondary && fixture.Catalog.MapVersion == "2.321" && window.Topmost &&
                transform is not null && transform.Current.CanResize && hitTests.Values.All(value => value) &&
                grip is not null && Near(small, smallRequested) && Near(defaultAgain, initial) &&
                Near(large, largeRequested) && Near(initial, new Size(540, 480)) &&
                Near(beforeRefresh, afterRefresh) && Near(beforeRefresh, afterNativeMoveSize) &&
                Near(beforeRefresh, afterScaleReapply) && Near(beforeRefresh, afterShow) &&
                afterMove != beforeMove && surfacePass && adaptiveColumns && smallSurface.ScrollExercised &&
                smallSurface.CandidateScrollExtent > smallSurface.CandidateScrollViewport && displayClamp.SafeAfterLayout &&
                (style & WsExNoActivate) != 0 && mouseActivateResult == 3 &&
                foregroundBefore == foregroundAfter && foregroundAfter != handle &&
                (clickThroughStyle & WsExTransparent) != 0 && (restoredStyle & WsExTransparent) == 0,
            catalogVersion = fixture.Catalog.MapVersion, selectedWork = work, ownedRect, onSecondary,
            hwnd = handle.ToInt64(), topmost = window.Topmost,
            transformAvailable = transform is not null, canResize = transform?.Current.CanResize ?? false,
            nativeResizeMechanism = "UI Automation TransformPattern.Resize on the owned HWND",
            physicalMouseDragPerformed = false,
            physicalMouseDragLimitation = "A physical resize loop requires global cursor/input injection; the fixture deliberately sends no global mouse or game input.",
            hitTests, gripVisible = grip?.IsVisible ?? false,
            initial, smallRequested, small, defaultAgain, largeRequested, large,
            smallSurface, defaultSurface, largeSurface, adaptiveColumns, displayClamp,
            sameHandRetained = Near(beforeRefresh, afterRefresh),
            nativeMove = new { mechanism = "UI Automation TransformPattern.Move", beforeMove, afterMove,
                sizeRetained = Near(beforeRefresh, afterNativeMoveSize) },
            retainedAfterScaleReapply = Near(beforeRefresh, afterScaleReapply),
            retainedAfterHideShow = Near(beforeRefresh, afterShow),
            noActivateStyle = (style & WsExNoActivate) != 0,
            mouseActivateResult, foregroundUnchanged = foregroundBefore == foregroundAfter,
            fixtureDidNotActivate = foregroundAfter != handle,
            clickThroughEnabled = (clickThroughStyle & WsExTransparent) != 0,
            clickThroughDisabled = (restoredStyle & WsExTransparent) == 0,
            tinyWorkArea,
            ownedWindowsBeforeClose = Application.Current.Windows.Count
        };
        await File.WriteAllTextAsync(Path.Combine(output, "report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report.success ? 0 : 1;
    }

    private static async Task<SurfaceEvidence> ValidateSurfaceAsync(OverlayWindow window,
        bool exerciseScroll = false)
    {
        await RenderedAsync(window);
        var view = window.NormalView;
        var visuals = Find<DependencyObject>(view).ToArray();
        var scroll = visuals.OfType<ScrollViewer>().Single(item =>
            AutomationProperties.GetAutomationId(item) == "normal-candidate-scroll");
        var offset = scroll.VerticalOffset;
        if (exerciseScroll)
        {
            scroll.ScrollToEnd();
            await RenderedAsync(window);
            offset = scroll.VerticalOffset;
            scroll.ScrollToHome();
            await RenderedAsync(window);
        }
        var candidates = visuals.OfType<Button>().Where(button => button.IsVisible &&
            AutomationProperties.GetAutomationId(button).StartsWith("normal-candidate-", StringComparison.Ordinal)).ToArray();
        return new(
            Find<Button>(window).Count(button => button.IsVisible &&
                AutomationProperties.GetAutomationId(button).StartsWith("overlay-mode-", StringComparison.Ordinal)),
            ((TextBlock)window.FindName("GoalText")).IsVisible &&
                !string.IsNullOrWhiteSpace(((TextBlock)window.FindName("GoalText")).Text) &&
                ((Grid)view.Content).Children.OfType<StackPanel>().Any(header =>
                    Grid.GetRow(header) == 0 && header.IsVisible && header.ActualHeight > 0 &&
                    Find<TextBlock>(header).Any(title => title.IsVisible &&
                        title.ActualHeight > 0 && !string.IsNullOrWhiteSpace(title.Text))),
            visuals.OfType<ComboBox>().Any(combo => combo.IsVisible &&
                AutomationProperties.GetAutomationId(combo) == "normal-stage-picker"),
            visuals.OfType<ComboBox>().Any(combo => combo.IsVisible &&
                AutomationProperties.GetAutomationId(combo) == "normal-category-picker") ||
                visuals.OfType<Button>().Any(button => button.IsVisible &&
                    AutomationProperties.GetAutomationId(button).StartsWith("normal-category-", StringComparison.Ordinal)) ||
                visuals.OfType<Expander>().Any(expander => expander.IsVisible &&
                    AutomationProperties.GetAutomationId(expander).StartsWith("overlay-category-", StringComparison.Ordinal)),
            candidates.Length,
            candidates.Select(AutomationProperties.GetName).Where(name => !string.IsNullOrWhiteSpace(name)).Take(8).ToArray(),
            Find<UniformGrid>(view).Where(grid => grid.IsVisible && grid.Children.Count > 0)
                .Select(grid => grid.Columns).Distinct().Order().ToArray(),
            scroll.ExtentHeight, scroll.ViewportHeight, exerciseScroll && offset > 0,
            ((TextBlock)window.FindName("StatusText")).IsVisible &&
                ((TextBlock)window.FindName("StatusText")).Text.Contains("긴 한국어", StringComparison.Ordinal));
    }

    private static Dictionary<string, bool> HitTests(IntPtr handle, Window window)
    {
        var source = PresentationSource.FromVisual(window);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var topLeft = window.PointToScreen(new Point(0, 0));
        var width = window.ActualWidth * toDevice.M11;
        var height = window.ActualHeight * toDevice.M22;
        return new()
        {
            ["left"] = Hit(handle, topLeft.X + 2, topLeft.Y + height / 2) == 10,
            ["right"] = Hit(handle, topLeft.X + width - 2, topLeft.Y + height / 2) == 11,
            ["top"] = Hit(handle, topLeft.X + width / 2, topLeft.Y + 2) == 12,
            ["bottom"] = Hit(handle, topLeft.X + width / 2, topLeft.Y + height - 2) == 15,
            ["topLeft"] = Hit(handle, topLeft.X + 2, topLeft.Y + 2) == 13,
            ["topRight"] = Hit(handle, topLeft.X + width - 2, topLeft.Y + 2) == 14,
            ["bottomLeft"] = Hit(handle, topLeft.X + 2, topLeft.Y + height - 2) == 16,
            ["bottomRight"] = Hit(handle, topLeft.X + width - 2, topLeft.Y + height - 2) == 17
        };
    }

    private static int Hit(IntPtr handle, double x, double y)
    {
        var packed = (ushort)(short)Math.Round(x) | ((int)(short)Math.Round(y) << 16);
        return (int)SendMessage(handle, WmNcHitTest, IntPtr.Zero, (IntPtr)packed);
    }

    private static async Task<DisplayClampEvidence> ExerciseDisplayClampAsync(OverlayWindow window, Rectangle physicalWork)
    {
        var scale = VisualTreeHelper.GetDpi(window);
        var work = new Rect(physicalWork.Left / scale.DpiScaleX, physicalWork.Top / scale.DpiScaleY,
            physicalWork.Width / scale.DpiScaleX, physicalWork.Height / scale.DpiScaleY);
        window.Left = work.Left + 24;
        window.Top = work.Top + 24;
        window.Width = work.Width + 96;
        await RenderedAsync(window);
        var oversizedActual = window.ActualWidth;
        window.EnsureVisible();
        var widthImmediatelyAfterClamp = window.Width;
        var actualImmediatelyAfterClamp = window.ActualWidth;
        await RenderedAsync(window);
        var final = SizeOf(window);
        var safe = final.Width <= work.Width + 1 && final.Height <= work.Height + 1 &&
            window.Left >= work.Left - 1 && window.Top >= work.Top - 1 &&
            window.Left + final.Width <= work.Right + 1 && window.Top + final.Height <= work.Bottom + 1;
        return new(work.Width, work.Height, oversizedActual, widthImmediatelyAfterClamp,
            actualImmediatelyAfterClamp, final.Width, final.Height, safe);
    }

    private static async Task<Size> ResizeAsync(Window window, TransformPattern transform, Size requested)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SizeChangedEventHandler handler = (_, _) => { if (Near(SizeOf(window), requested)) changed.TrySetResult(); };
        window.SizeChanged += handler;
        try
        {
            transform.Resize(requested.Width, requested.Height);
            if (!Near(SizeOf(window), requested)) await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await RenderedAsync(window);
            return SizeOf(window);
        }
        finally { window.SizeChanged -= handler; }
    }

    private static async Task<Point> MoveAsync(Window window, TransformPattern transform, Point requested)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) =>
        {
            if (Math.Abs(window.Left - requested.X) < 2 && Math.Abs(window.Top - requested.Y) < 2)
                changed.TrySetResult();
        };
        window.LocationChanged += handler;
        try
        {
            transform.Move(requested.X, requested.Y);
            if (Math.Abs(window.Left - requested.X) >= 2 || Math.Abs(window.Top - requested.Y) >= 2)
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return new(window.Left, window.Top);
        }
        finally { window.LocationChanged -= handler; }
    }

    private static async Task RenderedAsync(Window window)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => rendered.TrySetResult();
        CompositionTarget.Rendering += handler;
        try
        {
            window.InvalidateVisual();
            await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        finally { CompositionTarget.Rendering -= handler; }
    }

    private static Size SizeOf(Window window) => new(window.ActualWidth, window.ActualHeight);
    private static bool Near(Size first, Size second) =>
        Math.Abs(first.Width - second.Width) < 2 && Math.Abs(first.Height - second.Height) < 2;

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var nested in Find<T>(VisualTreeHelper.GetChild(root, index))) yield return nested;
    }

    private static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private sealed record UnitCheckFixture(DataCatalog Catalog, NormalCandidateBrowser Model,
        IReadOnlyDictionary<string, int> Inventory);
    private sealed record SurfaceEvidence(int ModeTabCount, bool HeaderVisible, bool StagePickerVisible,
        bool CategoryControlVisible, int CandidateCount, string[] CandidateNames, int[] Columns,
        double CandidateScrollExtent, double CandidateScrollViewport, bool ScrollExercised, bool FooterVisible);
    private sealed record TinyWorkAreaAssessment(double WorkWidth, double WorkHeight,
        double ReadableMinWidth, double ReadableMinHeight, bool ReadableMinimumFits, string Finding);
    private sealed record DisplayClampEvidence(double WorkWidth, double WorkHeight,
        double OversizedActualWidth, double WidthImmediatelyAfterClamp,
        double ActualWidthImmediatelyAfterClamp, double FinalWidth, double FinalHeight,
        bool SafeAfterLayout);

    private static Rectangle BoundsOf(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("Owned HWND bounds unavailable.");
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PointNative(int X, int Y);
    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(PointNative point, uint flags);
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RectNative rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
