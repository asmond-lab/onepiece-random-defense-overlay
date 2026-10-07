using System.Reflection;
using System.Windows.Media;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class RandyPickThemeTests
{
    [Fact]
    public void SharedBrushesAreFrozenAndRoutineColorsAreNeutral()
    {
        var brushes = typeof(RandyPickTheme).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(SolidColorBrush));
        foreach (var property in brushes)
        {
            var brush = Assert.IsType<SolidColorBrush>(property.GetValue(null));
            Assert.True(brush.IsFrozen, property.Name);
            if (property.Name is nameof(RandyPickTheme.Warning) or nameof(RandyPickTheme.Danger)) continue;
            Assert.Equal(brush.Color.R, brush.Color.G);
            Assert.Equal(brush.Color.G, brush.Color.B);
        }
    }

    [Fact]
    public void SmallTextAndCheckedGlyphRemainReadable()
    {
        foreach (var foreground in new[] { RandyPickTheme.Text, RandyPickTheme.Secondary, RandyPickTheme.Muted })
        foreach (var background in new[] { RandyPickTheme.Canvas, RandyPickTheme.Surface, RandyPickTheme.Raised })
            Assert.True(Contrast(foreground.Color, background.Color) >= 4.5,
                $"Insufficient text contrast: {foreground.Color} on {background.Color}");

        Assert.True(Contrast(RandyPickTheme.CanvasColor, RandyPickTheme.AccentColor) >= 4.5);
        Assert.True(Contrast(RandyPickTheme.SelectionBorderColor, RandyPickTheme.SelectionColor) >= 3);
    }

    [Fact]
    public void NativeColorReferenceUsesBgrOrdering()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56));
        Assert.Equal(0x563412, RandyPickTheme.ToColorRef(brush));
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
