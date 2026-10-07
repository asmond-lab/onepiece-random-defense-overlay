using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class AppButtonBorderBindingTests
{
    [Fact]
    public void SharedButtonTemplateActuallyRendersConfiguredBorders()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "App.xaml"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var document = XDocument.Load(Path.Combine(directory!.FullName, "App.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var body = Assert.Single(document.Descendants(wpf + "Border"), element => (string?)element.Attribute(x + "Name") == "ButtonBody");
        Assert.Equal("{TemplateBinding BorderBrush}", (string?)body.Attribute("BorderBrush"));
        Assert.Equal("{TemplateBinding BorderThickness}", (string?)body.Attribute("BorderThickness"));
        Assert.Equal("{TemplateBinding Background}", (string?)body.Attribute("Background"));
        var presenter = Assert.Single(body.Elements(wpf + "ContentPresenter"));
        Assert.Equal("{TemplateBinding HorizontalContentAlignment}", (string?)presenter.Attribute("HorizontalAlignment"));
        Assert.Equal("{TemplateBinding VerticalContentAlignment}", (string?)presenter.Attribute("VerticalAlignment"));
    }
}
