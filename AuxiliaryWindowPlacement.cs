using System.Windows;

namespace OrandOverlay;

public static class AuxiliaryWindowPlacement
{
    public const double PreferredWidth = 360;
    public const double PreferredHeight = 620;
    public const double Gap = 12;

    // All rectangles use one native desktop space. DPI scales size/gap, not origins.
    public static Rect Calculate(Rect owner, Rect workArea, double dpiScale)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScale));
        if (workArea.IsEmpty || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        var width = Math.Min(Math.Ceiling(PreferredWidth * dpiScale), workArea.Width);
        var height = Math.Min(Math.Ceiling(Math.Min(PreferredHeight * dpiScale, owner.Height)), workArea.Height);
        var left = Math.Clamp(Math.Round(owner.Left - width - Gap * dpiScale), workArea.Left, workArea.Right - width);
        var top = Math.Clamp(Math.Round(owner.Top), workArea.Top, workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }
}
