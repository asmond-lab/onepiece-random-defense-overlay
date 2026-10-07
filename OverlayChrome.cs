using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

/// <summary>
/// 인게임 오버레이 전용 배경 리소스. 앱 리소스에는 기존 불투명 색을, 오버레이 창 리소스에는
/// 80% 셸과 흰색 틴트를 넣어 같은 뷰가 메인 창에서는 불투명, 오버레이에서는 반투명으로 보이게 한다.
/// </summary>
internal static class OverlayChrome
{
    public const string ShellKey = "OverlayShellBrush";
    public const string CanvasKey = "OverlayCanvasBrush";
    public const string WellKey = "OverlayWellBrush";
    public const string RaisedKey = "OverlayRaisedBrush";
    public const string SelectionKey = "OverlaySelectionBrush";

    public static readonly string[] Keys = [ShellKey, CanvasKey, WellKey, RaisedKey, SelectionKey];

    /// <summary>
    /// 틴트 알파는 #181818 위 합성 결과가 기존 Surface/Raised/Selection 명도에 맞도록 고른 값:
    /// 24 + 231·a = 32 / 41 / 54.
    /// </summary>
    public static void ApplyTranslucent(ResourceDictionary target)
    {
        var c = RandyPickTheme.CanvasColor;
        target[ShellKey] = Frozen(Color.FromArgb((byte)Math.Round(RandyPickTheme.OverlayAlpha * 255), c.R, c.G, c.B));
        target[CanvasKey] = Brushes.Transparent;
        target[WellKey] = Frozen(Color.FromArgb(9, 255, 255, 255));
        target[RaisedKey] = Frozen(Color.FromArgb(19, 255, 255, 255));
        target[SelectionKey] = Frozen(Color.FromArgb(33, 255, 255, 255));
    }

    public static void ApplyOpaque(ResourceDictionary target)
    {
        target[ShellKey] = RandyPickTheme.Canvas;
        target[CanvasKey] = RandyPickTheme.Canvas;
        target[WellKey] = RandyPickTheme.Surface;
        target[RaisedKey] = RandyPickTheme.Raised;
        target[SelectionKey] = RandyPickTheme.Selection;
    }

    public static T WithOverlayBackground<T>(this T element, string key) where T : FrameworkElement
    {
        EnsureOpaqueDefaults();
        var property = element switch
        {
            Border => Border.BackgroundProperty,
            Control => Control.BackgroundProperty,
            Panel => Panel.BackgroundProperty,
            _ => throw new ArgumentException($"Unsupported background host: {element.GetType().Name}", nameof(element))
        };
        element.SetResourceReference(property, key);
        return element;
    }

    public static void EnsureOpaqueDefaults()
    {
        if (Application.Current is { } app && !app.Resources.Contains(ShellKey))
            ApplyOpaque(app.Resources);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
