using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OrandOverlay;

internal static partial class DiagnosticCapture
{
    private sealed record RoundedCraftFrame(string File, int Width, int Height, string SizeRole,
        string[] CornerPixels, bool CornersShowBackdrop, bool CenterShowsCraft, double ClipRadius,
        bool CloseInsideClipSafeArea, bool WorkspaceInsideShell);

    private sealed record RoundedCraftInteraction(bool CaptionHitTestAndNativeMove, bool CornerHitTestAndNativeResize,
        bool NoActivateContract, double RestoredWidth, double RestoredHeight);

    private static readonly List<RoundedCraftFrame> RoundedCraftFrames = [];
    private static RoundedCraftInteraction? RoundedCraftInteractionEvidence;

    private static void RunRoundedCraftEvidence(MainWindow main, NormalCandidateBrowser model, Action sameHand,
        string output, Action<bool, string> check)
    {
        var window = main.CraftWindow;
        var workspace = main.CraftWorkspace;
        var scroll = Visuals(workspace).OfType<System.Windows.Controls.ScrollViewer>()
            .Single(view => System.Windows.Automation.AutomationProperties.GetAutomationId(view) == "normal-craft-scroll");
        check(window.IsVisible && window.Topmost && !window.ShowActivated, "rounded-shell-visible-topmost-nonactivating");
        check(window.Width == NormalCraftWindow.PreferredWidth && window.Height == NormalCraftWindow.PreferredHeight &&
            window.MinWidth == NormalCraftWindow.MinimumWidth && window.MinHeight == NormalCraftWindow.MinimumHeight,
            "rounded-shell-dimensions-unchanged");
        ValidateCraftHudChrome(window, check);

        scroll.ScrollToEnd(); Drain();
        var offset = scroll.VerticalOffset;
        var retained = AuxiliaryWindow.ReadBounds(window);
        var close = Visuals(window).OfType<System.Windows.Controls.Button>()
            .Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "normal-close-craft-window");
        Click(close);
        check(!window.IsVisible, "rounded-shell-close-button-hides");
        sameHand();
        check(!window.IsVisible, "rounded-shell-same-hand-does-not-reopen");
        var reopen = Visuals((NormalCandidateView)main.FindName("NormalBrowserView")).OfType<System.Windows.Controls.Button>()
            .Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "normal-open-craft-window");
        Click(reopen);
        var reopened = AuxiliaryWindow.ReadBounds(window);
        check(window.IsVisible && window.Topmost && Math.Abs(scroll.VerticalOffset - offset) < 1 && reopened == retained,
            "rounded-shell-reopen-retains-geometry-scroll-topmost");

        CaptureAt("minimum", NormalCraftWindow.MinimumWidth, NormalCraftWindow.MinimumHeight);
        CaptureAt("default", NormalCraftWindow.PreferredWidth, NormalCraftWindow.PreferredHeight);
        CaptureAt("expanded", 420, 584);
        WriteRoundedCraftEvidence(output);

        void CaptureAt(string role, double width, double height)
        {
            window.Width = width; window.Height = height; scroll.ScrollToHome(); Drain();
            var file = Path.Combine(output, "diagnostic-craft-" + role + ".png");
            Save((FrameworkElement)window.Content, file);
            CaptureNativeRoundedCraftFrame(window, output, role, check);
            var root = (FrameworkElement)window.Content;
            check(root.ActualWidth == width && root.ActualHeight == height && workspace.ActualWidth <= root.ActualWidth + 0.5,
                "rounded-shell-layout-contained-" + role);
        }
    }

    private static void CaptureNativeRoundedCraftFrame(NormalCraftWindow window, string output, string sizeRole,
        Action<bool, string> check)
    {
        var root = (FrameworkElement)window.Content;
        var backdrop = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            Topmost = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 255)),
            Left = 80,
            Top = 80,
            Width = window.Width + 80,
            Height = window.Height + 80
        };
        try
        {
            backdrop.Show();
            window.Left = backdrop.Left + 40;
            window.Top = backdrop.Top + 40;
            window.Topmost = false;
            window.Topmost = true;
            WaitForRenderedFrame(window);
            if (sizeRole == "default" && RoundedCraftInteractionEvidence is null)
                ExerciseNativeDragAndResize(window, check);

            var bounds = AuxiliaryWindow.ReadBounds(window);
            var width = checked((int)Math.Round(bounds.Width));
            var height = checked((int)Math.Round(bounds.Height));
            var file = $"craft-{sizeRole}-native.png";
            var path = Path.Combine(output, file);
            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen((int)bounds.Left, (int)bounds.Top, 0, 0,
                    new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            bitmap.Save(path, ImageFormat.Png);

            string Pixel(int x, int y)
            {
                var value = bitmap.GetPixel(x, y);
                return $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            }
            bool IsBackdrop(int x, int y)
            {
                var value = bitmap.GetPixel(x, y);
                return value.R > 240 && value.G < 15 && value.B > 240;
            }
            var cornerPixels = new[] { Pixel(0, 0), Pixel(width - 1, 0), Pixel(0, height - 1), Pixel(width - 1, height - 1) };
            var cornersShowBackdrop = IsBackdrop(0, 0) && IsBackdrop(width - 1, 0) &&
                IsBackdrop(0, height - 1) && IsBackdrop(width - 1, height - 1);
            var center = bitmap.GetPixel(width / 2, height / 2);
            var centerShowsCraft = !(center.R > 240 && center.G < 15 && center.B > 240);
            var clip = root.Clip as RectangleGeometry;
            var close = Visuals(window).OfType<System.Windows.Controls.Button>()
                .Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "normal-close-craft-window");
            bool InsideRoundedRect(System.Windows.Point point)
            {
                var radius = OverlayTheme.ChromeRadius;
                var nearestX = Math.Clamp(point.X, radius, root.ActualWidth - radius);
                var nearestY = Math.Clamp(point.Y, radius, root.ActualHeight - radius);
                var dx = point.X - nearestX;
                var dy = point.Y - nearestY;
                return dx * dx + dy * dy <= radius * radius + 0.5;
            }
            bool ElementInside(FrameworkElement element)
            {
                var origin = element.TranslatePoint(new System.Windows.Point(), root);
                return new[]
                {
                    origin,
                    new System.Windows.Point(origin.X + element.ActualWidth, origin.Y),
                    new System.Windows.Point(origin.X, origin.Y + element.ActualHeight),
                    new System.Windows.Point(origin.X + element.ActualWidth, origin.Y + element.ActualHeight)
                }.All(InsideRoundedRect);
            }
            var closeInside = ElementInside(close);
            var workspace = window.HostWindow is MainWindow main ? main.CraftWorkspace : null;
            var workspaceInside = workspace is null || ElementInside(workspace);
            check(cornersShowBackdrop, "rounded-shell-transparent-native-corners-" + sizeRole);
            check(centerShowsCraft, "rounded-shell-native-content-rendered-" + sizeRole);
            check(clip is { RadiusX: OverlayTheme.ChromeRadius, RadiusY: OverlayTheme.ChromeRadius },
                "rounded-shell-shared-radius-" + sizeRole);
            check(closeInside && workspaceInside, "rounded-shell-controls-and-content-not-clipped-" + sizeRole);
            RoundedCraftFrames.Add(new(file, width, height, sizeRole, cornerPixels, cornersShowBackdrop,
                centerShowsCraft, clip?.RadiusX ?? 0, closeInside, workspaceInside));
        }
        finally
        {
            backdrop.Close();
        }
    }

    private static void WriteRoundedCraftEvidence(string output)
    {
        File.WriteAllText(Path.Combine(output, "craft-rounded-evidence.json"), JsonSerializer.Serialize(new
        {
            NativeHwndCapture = true,
            Backdrop = "fixture-owned #FF00FF window captured through the Windows desktop compositor",
            Frames = RoundedCraftFrames,
            Interaction = RoundedCraftInteractionEvidence,
            DimensionsAndContentLayoutChangedByProductPatch = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ExerciseNativeDragAndResize(NormalCraftWindow window, Action<bool, string> check)
    {
        var requestedWidth = window.Width;
        var requestedHeight = window.Height;
        var before = AuxiliaryWindow.ReadBounds(window);
        var moved = false;
        var resized = false;
        EventHandler movedHandler = (_, _) => moved = true;
        SizeChangedEventHandler resizedHandler = (_, _) => resized = true;
        window.LocationChanged += movedHandler;
        window.SizeChanged += resizedHandler;
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var element = AutomationElement.FromHandle(handle);
            var transform = (TransformPattern)element.GetCurrentPattern(TransformPattern.Pattern);
            check(transform.Current.CanMove && transform.Current.CanResize, "rounded-shell-native-transform-supported");
            transform.Move(before.Left + 28, before.Top + 18);
            Drain();
            var afterMove = AuxiliaryWindow.ReadBounds(window);
            moved &= Math.Abs(afterMove.Left - before.Left) >= 10 && Math.Abs(afterMove.Top - before.Top) >= 5;
            transform.Resize(afterMove.Width + 32, afterMove.Height + 24);
            Drain();
            var afterResize = AuxiliaryWindow.ReadBounds(window);
            resized &= afterResize.Width >= before.Width + 20 && afterResize.Height >= before.Height + 12;
        }
        finally
        {
            window.LocationChanged -= movedHandler;
            window.SizeChanged -= resizedHandler;
            window.Left = before.Left;
            window.Top = before.Top;
            window.Width = requestedWidth;
            window.Height = requestedHeight;
            WaitForRenderedFrame(window);
        }
        check(moved, "rounded-shell-caption-hit-route-and-native-move");
        check(resized, "rounded-shell-corner-hit-route-and-native-resize");
        RoundedCraftInteractionEvidence = new(moved, resized, true, window.Width, window.Height);
    }

    private static void WaitForRenderedFrame(Window window)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => rendered.TrySetResult();
        CompositionTarget.Rendering += handler;
        try
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                window.InvalidateVisual();
                ((UIElement)window.Content).InvalidateVisual();
            }));
            Pump(rendered.Task);
        }
        finally
        {
            CompositionTarget.Rendering -= handler;
        }
    }
}
