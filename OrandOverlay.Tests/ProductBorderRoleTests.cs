using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class ProductBorderRoleTests
{
    [Fact]
    public void BorderRolesAreNeutralAndFocusIsDistinct()
    {
        foreach (var brush in new[] { OverlayTheme.OutlineBrush, OverlayTheme.SelectionBorderBrush,
                     OverlayTheme.FocusBrush, (SolidColorBrush)MainPlanTheme.GoalLine })
        {
            var c = brush.Color;
            Assert.Equal(c.R, c.G);
            Assert.Equal(c.G, c.B);
        }
        Assert.Same(OverlayTheme.OutlineBrush, MainPlanTheme.GoalLine);
        Assert.NotEqual(OverlayTheme.FocusBrush.Color, OverlayTheme.SelectionBorderBrush.Color);
        Assert.NotEqual(OverlayTheme.FocusBrush.Color, OverlayTheme.GoldBrush.Color);
    }

    [Fact]
    public void SharedControlAccentUsesFocusColorAndXamlBordersNeverUseGold()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "App.xaml"))) root = root.Parent;
        Assert.NotNull(root);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var app = XDocument.Load(Path.Combine(root.FullName, "App.xaml"));
        var accent = Assert.Single(app.Descendants(wpf + "SolidColorBrush"), item => (string?)item.Attribute(x + "Key") == "AccentBrush");
        Assert.Equal("{x:Static local:RandyPickTheme.AccentColor}", (string?)accent.Attribute("Color"));
        Assert.Equal(OverlayTheme.FocusBrush.Color, RandyPickTheme.AccentColor);
        var checkMark = Assert.Single(app.Descendants(wpf + "Path"), item => (string?)item.Attribute(x + "Name") == "CheckMark");
        Assert.Equal("{x:Static local:RandyPickTheme.Canvas}", (string?)checkMark.Attribute("Stroke"));
        foreach (var file in new[] { "MainWindow.xaml", "BeginnerCoachView.xaml", "OverlayWindow.xaml", "StatsOverlayWindow.xaml", "TelemetryConsentWindow.xaml", "App.xaml" })
        {
            var document = XDocument.Load(Path.Combine(root.FullName, file));
            var values = document.Descendants().SelectMany(item => item.Attributes().Where(attribute => attribute.Name.LocalName == "BorderBrush").Select(attribute => attribute.Value))
                .Concat(document.Descendants(wpf + "Setter").Where(item => (string?)item.Attribute("Property") == "BorderBrush").Select(item => (string)item.Attribute("Value")!));
            Assert.All(values, value => Assert.DoesNotContain("Gold", value, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task StaticCraftAndKeycapOutlinesAreRestrained()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var featured = OverlayTheme.FeaturedShell(new TextBlock());
                Assert.Same(OverlayTheme.OutlineBrush, featured.BorderBrush);
                Assert.True(featured.BorderThickness.Left <= 1);
                var key = Assert.IsType<Border>(OverlayTheme.Keycap("K", true));
                Assert.Same(OverlayTheme.SelectionBorderBrush, key.BorderBrush);
                Assert.Equal(new Thickness(1), key.BorderThickness);
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
