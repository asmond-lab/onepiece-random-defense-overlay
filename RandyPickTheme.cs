using System.Windows.Media;

namespace OrandOverlay;

/// <summary>Shared neutral colors for the desktop window and game overlays.</summary>
internal static class RandyPickTheme
{
    public const string CanvasHex = "#181818";
    public const string SurfaceHex = "#202020";
    public const string RaisedHex = "#292929";
    public const string HoverHex = "#333333";
    public const string PressedHex = "#3B3B3B";
    public const string BorderHex = "#3C3C3C";
    public const string StrongBorderHex = "#737373";
    public const string TextHex = "#F3F3F3";
    public const string SecondaryHex = "#C2C2C2";
    public const string MutedHex = "#969696";
    public const string AccentHex = "#FFFFFF";
    public const string SelectionHex = "#363636";
    public const string SelectionBorderHex = "#E0E0E0";
    public const string SuccessHex = "#D4D4D4";
    public const string WarningHex = "#E4B875";
    public const string DangerHex = "#E99393";
    public const double OverlayAlpha = 0.80;

    public static SolidColorBrush Canvas { get; } = Create(CanvasHex);
    public static SolidColorBrush Surface { get; } = Create(SurfaceHex);
    public static SolidColorBrush Raised { get; } = Create(RaisedHex);
    public static SolidColorBrush Hover { get; } = Create(HoverHex);
    public static SolidColorBrush Pressed { get; } = Create(PressedHex);
    public static SolidColorBrush Border { get; } = Create(BorderHex);
    public static SolidColorBrush StrongBorder { get; } = Create(StrongBorderHex);
    public static SolidColorBrush Text { get; } = Create(TextHex);
    public static SolidColorBrush Secondary { get; } = Create(SecondaryHex);
    public static SolidColorBrush Muted { get; } = Create(MutedHex);
    public static SolidColorBrush Accent { get; } = Create(AccentHex);
    public static SolidColorBrush Selection { get; } = Create(SelectionHex);
    public static SolidColorBrush SelectionBorder { get; } = Create(SelectionBorderHex);
    public static SolidColorBrush Success { get; } = Create(SuccessHex);
    public static SolidColorBrush Warning { get; } = Create(WarningHex);
    public static SolidColorBrush Danger { get; } = Create(DangerHex);
    public static SolidColorBrush OverlayPanel { get; } = Create(Color.FromArgb(247, 32, 32, 32));
    public static SolidColorBrush Shadow { get; } = Create(Color.FromArgb(170, 0, 0, 0));

    // XAML Color properties receive actual Color values, without string conversion.
    public static Color CanvasColor => Canvas.Color;
    public static Color SurfaceColor => Surface.Color;
    public static Color RaisedColor => Raised.Color;
    public static Color HoverColor => Hover.Color;
    public static Color PressedColor => Pressed.Color;
    public static Color BorderColor => Border.Color;
    public static Color StrongBorderColor => StrongBorder.Color;
    public static Color TextColor => Text.Color;
    public static Color SecondaryColor => Secondary.Color;
    public static Color MutedColor => Muted.Color;
    public static Color AccentColor => Accent.Color;
    public static Color SelectionColor => Selection.Color;
    public static Color SelectionBorderColor => SelectionBorder.Color;
    public static Color SuccessColor => Success.Color;
    public static Color WarningColor => Warning.Color;
    public static Color DangerColor => Danger.Color;
    public static Color OverlayPanelColor => OverlayPanel.Color;
    public static Color ShadowColor => Shadow.Color;

    public static int ToColorRef(SolidColorBrush brush) =>
        brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);

    private static SolidColorBrush Create(string hex) => Create((Color)ColorConverter.ConvertFromString(hex));

    private static SolidColorBrush Create(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
