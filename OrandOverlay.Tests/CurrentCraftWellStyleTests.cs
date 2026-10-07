using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class CurrentCraftWellStyleTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void MainAndOverlayCurrentCraftWellsUseSharedTokenStyle()
    {
        var main = XDocument.Load(Path.Combine(Root, "MainWindow.xaml"));
        var overlay = XDocument.Load(Path.Combine(Root, "OverlayWindow.xaml"));

        Assert.Equal("{StaticResource CurrentCraftWell}",
            CurrentCraftWell(main).Attribute("Style")?.Value);
        Assert.Equal("{StaticResource CurrentCraftWell}",
            CurrentCraftWell(overlay).Attribute("Style")?.Value);
    }

    [Fact]
    public void SharedCurrentCraftWellStyleUsesOverlayThemeTokens()
    {
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var app = XDocument.Load(Path.Combine(Root, "App.xaml"));
        var style = app.Descendants(presentation + "Style").Single(element =>
            element.Attribute(x + "Key")?.Value == "CurrentCraftWell");
        var setters = style.Elements(presentation + "Setter").ToDictionary(
            element => element.Attribute("Property")!.Value,
            element => element.Attribute("Value")!.Value,
            StringComparer.Ordinal);

        Assert.Equal("{x:Static local:OverlayTheme.FeaturedBrush}",
            setters["Background"]);
        Assert.Equal("{x:Static local:OverlayTheme.OutlineBrush}",
            setters["BorderBrush"]);
        Assert.Equal("{x:Static local:OverlayTheme.WellCornerRadius}",
            setters["CornerRadius"]);
        Assert.Equal("{x:Static local:OverlayTheme.PlannerBlockPadding}",
            setters["Padding"]);
    }

    private static XElement CurrentCraftWell(XDocument document)
    {
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var panel = document.Descendants(presentation + "StackPanel").Single(element =>
            element.Attribute(x + "Name")?.Value == "NowPanel");
        return Assert.IsType<XElement>(panel.Parent);
    }
}
