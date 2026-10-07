using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal sealed record NativeGuiEvidence(
    long Hwnd,
    bool ContentRendered,
    bool Minimized,
    bool Restored,
    bool ScreenshotWritten,
    string ScreenshotPath,
    IReadOnlyList<NativeGuiAction> Actions);

internal sealed record NativeGuiAction(
    string Action,
    string RequestedState,
    string ObservedState,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WindowVisualState UiaState);

internal static class NativeGuiCapture
{
    internal static async Task<NativeGuiEvidence> RunAsync(Window window, string output)
    {
        var contentRendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.ContentRendered += ContentRendered;
        window.Show();
        await contentRendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        window.ContentRendered -= ContentRendered;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Fixture window did not create an HWND.");
        var element = AutomationElement.FromHandle(hwnd);
        var pattern = (WindowPattern)element.GetCurrentPattern(WindowPattern.Pattern);
        var actions = new List<NativeGuiAction>();

        var minimized = await SetStateAsync(window, element, pattern, WindowVisualState.Minimized);
        actions.Add(new("WindowPattern.SetWindowVisualState", "Minimized", minimized.ToString(),
            pattern.Current.WindowVisualState));

        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler rendering = (_, _) => rendered.TrySetResult();
        CompositionTarget.Rendering += rendering;
        var restored = await SetStateAsync(window, element, pattern, WindowVisualState.Normal);
        await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        CompositionTarget.Rendering -= rendering;
        actions.Add(new("WindowPattern.SetWindowVisualState", "Normal", restored.ToString(),
            pattern.Current.WindowVisualState));

        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        var screenshotPath = Path.Combine(output, "fixture.png");
        SaveScreenshot(window, screenshotPath);
        return new(hwnd.ToInt64(), true, minimized == WindowState.Minimized, restored == WindowState.Normal,
            File.Exists(screenshotPath), screenshotPath, actions);

        void ContentRendered(object? sender, EventArgs args) => contentRendered.TrySetResult();
    }

    private static async Task<WindowState> SetStateAsync(
        Window window,
        AutomationElement element,
        WindowPattern pattern,
        WindowVisualState requested)
    {
        var expected = requested == WindowVisualState.Minimized ? WindowState.Minimized : WindowState.Normal;
        var changed = new TaskCompletionSource<WindowState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiaChanged = new TaskCompletionSource<WindowVisualState>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler stateChanged = (_, _) =>
        {
            if (window.WindowState == expected) changed.TrySetResult(window.WindowState);
        };
        AutomationPropertyChangedEventHandler automationChanged = (_, args) =>
        {
            if (args.Property == WindowPattern.WindowVisualStateProperty &&
                args.NewValue is WindowVisualState state &&
                state == requested)
                uiaChanged.TrySetResult(state);
        };

        window.StateChanged += stateChanged;
        Automation.AddAutomationPropertyChangedEventHandler(
            element, TreeScope.Element, automationChanged, WindowPattern.WindowVisualStateProperty);
        try
        {
            pattern.SetWindowVisualState(requested);
            var observed = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await uiaChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return observed;
        }
        finally
        {
            Automation.RemoveAutomationPropertyChangedEventHandler(element, automationChanged);
            window.StateChanged -= stateChanged;
        }
    }

    private static void SaveScreenshot(Window window, string path)
    {
        var width = checked((int)Math.Ceiling(window.ActualWidth));
        var height = checked((int)Math.Ceiling(window.ActualHeight));
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Fixture has no rendered bounds.");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        encoder.Save(stream);
    }
}
