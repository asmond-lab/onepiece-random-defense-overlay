using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OrandOverlay;

/// <summary>
/// 화면 해상도에 따른 UI 자동 배율. 기본 크기(720×700 오버레이)가 2K(논리 높이
/// 1440)에서 다듬어졌으므로 2560×1440을 1.0 기준으로 삼고, 더 작거나 큰 화면에서
/// 같은 화면 비율을 차지하도록 비례 조정한다(FHD 100% → 0.75배, 4K 100% → 1.5배).
/// 비표준 종횡비에서는 가로·세로 중 더 제한적인 축을 사용해 화면을 과도하게
/// 가리지 않는다. 윈도우 배율(DPI)이 이미 반영된 논리 좌표를 쓰므로 배율을 올려
/// 쓰는 사용자는 이중으로 커지지 않는다.
/// </summary>
public static class UiScale
{
    public const double BaselineWidth = 2560.0;
    public const double BaselineHeight = 1440.0;

    public static double ForWindow(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            var screen = handle == IntPtr.Zero
                ? System.Windows.Forms.Screen.PrimaryScreen
                : System.Windows.Forms.Screen.FromHandle(handle);
            if (screen is null) return 1.0;
            var dpi = VisualTreeHelper.GetDpi(window);
            return FromScreen(screen.Bounds.Width, screen.Bounds.Height,
                dpi.DpiScaleX, dpi.DpiScaleY);
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>16:9 화면의 물리 픽셀 높이와 윈도우 배율(DPI)로 배율을 계산한다.</summary>
    public static double FromScreen(double physicalHeight, double dpiScale)
        => FromScreen(physicalHeight * BaselineWidth / BaselineHeight, physicalHeight,
            dpiScale, dpiScale);

    /// <summary>물리 픽셀 크기와 축별 윈도우 배율(DPI)로 배율을 계산한다.</summary>
    public static double FromScreen(double physicalWidth, double physicalHeight,
        double dpiScaleX, double dpiScaleY)
    {
        var logicalWidth = physicalWidth / Math.Max(0.5, dpiScaleX);
        var logicalHeight = physicalHeight / Math.Max(0.5, dpiScaleY);
        var scale = Math.Min(logicalWidth / BaselineWidth, logicalHeight / BaselineHeight);
        return Math.Clamp(scale, 0.5, 3.0);
    }

    /// <summary>창 내용에 배율을 적용하고 창의 기준 크기도 같은 비율로 맞춘다.</summary>
    public static double Apply(Window window, double baseWidth, double baseHeight)
    {
        var scale = ForWindow(window);
        if (window.Content is FrameworkElement root)
            root.LayoutTransform = Math.Abs(scale - 1.0) < 0.01
                ? Transform.Identity
                : new ScaleTransform(scale, scale);
        window.Width = baseWidth * scale;
        window.Height = baseHeight * scale;
        return scale;
    }
}
