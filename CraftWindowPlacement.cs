using System.Windows;

namespace OrandOverlay;

internal static class CraftWindowPlacement
{
    internal const double Gap = 8;

    // All rectangles are native screen pixels. Only desiredSize and the minimum are DIPs;
    // never scale a virtual desktop origin (which may be negative) by the target DPI.
    internal static Rect Calculate(Rect stats, Rect workArea, Size desiredSize, double dpiScale = 1)
    {
        var size = FitSize(desiredSize, workArea, dpiScale);
        var gap = Gap * Scale(dpiScale);
        var below = stats.Bottom + gap;
        var minimumHeight = Math.Min(NormalCraftWindow.MinimumHeight * Scale(dpiScale), workArea.Height);
        if (workArea.Bottom - below >= minimumHeight)
        {
            // Keep the natural below-stats relationship even when a shorter window is needed.
            size.Height = Math.Min(size.Height, workArea.Bottom - below);
            return Clamp(new Rect(stats.Left, below, size.Width, size.Height), workArea);
        }
        if (stats.Top - gap - size.Height >= workArea.Top)
            return Clamp(new Rect(stats.Left, stats.Top - gap - size.Height, size.Width, size.Height), workArea);
        if (stats.Right + gap + size.Width <= workArea.Right)
            return Clamp(new Rect(stats.Right + gap, stats.Top, size.Width, size.Height), workArea);
        if (stats.Left - gap - size.Width >= workArea.Left)
            return Clamp(new Rect(stats.Left - gap - size.Width, stats.Top, size.Width, size.Height), workArea);
        return Clamp(new Rect(stats.Left, below, size.Width, size.Height), workArea);
    }

    internal static Rect Restore(CraftWindowGeometry saved, Rect workArea, double dpiScale = 1)
    {
        var size = FitSize(new Size(saved.WidthDip, saved.HeightDip), workArea, dpiScale);
        var left = double.IsFinite(saved.LeftPixels) ? saved.LeftPixels : workArea.Left;
        var top = double.IsFinite(saved.TopPixels) ? saved.TopPixels : workArea.Top;
        return Clamp(new Rect(left, top, size.Width, size.Height), workArea);
    }

    internal static Size FitSize(Size desiredSize, Rect workArea, double dpiScale)
    {
        if (workArea.IsEmpty || !double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height) ||
            !double.IsFinite(workArea.Left) || !double.IsFinite(workArea.Top) || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentException("A finite nonempty monitor work area is required.", nameof(workArea));
        var scale = Scale(dpiScale);
        var width = double.IsFinite(desiredSize.Width) && desiredSize.Width > 0 ? desiredSize.Width : NormalCraftWindow.PreferredWidth;
        var height = double.IsFinite(desiredSize.Height) && desiredSize.Height > 0 ? desiredSize.Height : NormalCraftWindow.PreferredHeight;
        return new Size(
            Math.Clamp(width * scale, Math.Min(NormalCraftWindow.MinimumWidth * scale, workArea.Width), workArea.Width),
            Math.Clamp(height * scale, Math.Min(NormalCraftWindow.MinimumHeight * scale, workArea.Height), workArea.Height));
    }

    private static Rect Clamp(Rect bounds, Rect workArea) => new(
        Math.Clamp(bounds.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - bounds.Width)),
        Math.Clamp(bounds.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - bounds.Height)),
        bounds.Width, bounds.Height);

    internal static double Scale(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;
}
