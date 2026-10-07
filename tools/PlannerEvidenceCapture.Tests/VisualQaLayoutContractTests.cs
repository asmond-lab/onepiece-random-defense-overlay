using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace PlannerEvidenceCapture.Tests;

public sealed class VisualQaLayoutContractTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Load(string name, [CallerFilePath] string source = "") =>
        XDocument.Load(Path.Combine(Path.GetDirectoryName(source)!, "../..", name));

    [Fact]
    public void SharedTemplatesKeepHitAreaAndKeyboardBindings()
    {
        var app = Load("App.xaml");
        var combo = app.Descendants(Ui + "Style").Single(e => (string?)e.Attribute("TargetType") == "ComboBox");
        Assert.Contains(combo.Elements(Ui + "Setter"), e => (string?)e.Attribute("Property") == "MinHeight"
            && (string?)e.Attribute("Value") == "{x:Static local:OverlayTheme.ControlMinHitHeight}");
        Assert.Contains(combo.Descendants(Ui + "Border"), e => (string?)e.Attribute("Padding") == "{TemplateBinding Padding}");
        var expander = app.Descendants(Ui + "Style").Single(e => (string?)e.Attribute("TargetType") == "Expander");
        var toggle = expander.Descendants(Ui + "ToggleButton").Single();
        Assert.Equal("{TemplateBinding Foreground}", (string?)toggle.Attribute("Foreground"));
        Assert.Equal("{x:Static local:OverlayTheme.ControlMinHitHeight}", (string?)toggle.Attribute("MinHeight"));
        Assert.Equal("True", (string?)toggle.Attribute("Focusable"));
        Assert.Equal("True", (string?)toggle.Attribute("IsTabStop"));
        Assert.Contains("Mode=TwoWay", (string)toggle.Attribute("IsChecked")!);
        Assert.Contains(expander.Descendants(Ui + "Trigger"), e => (string?)e.Attribute("Property") == "IsKeyboardFocused");
    }

    [Fact]
    public void CoachActionsAreOutsideBodyScrollInAutoFooter()
    {
        var coach = Load("BeginnerCoachView.xaml");
        foreach (var id in new[] { "coach-pause", "coach-review" })
        {
            var button = coach.Descendants(Ui + "Button").Single(e => (string?)e.Attribute("AutomationProperties.AutomationId") == id);
            Assert.DoesNotContain(button.Ancestors(), e => e.Name == Ui + "ScrollViewer");
            var footer = button.Parent!;
            var row = int.Parse((string)footer.Attribute("Grid.Row")!);
            Assert.Equal("Auto", (string?)footer.Parent!.Element(Ui + "Grid.RowDefinitions")!.Elements().ElementAt(row).Attribute("Height"));
            Assert.NotNull(button.Attribute("Click"));
        }
        Assert.Single(coach.Descendants(Ui + "ScrollViewer"));
        Assert.NotNull(coach.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "PauseButton"));
    }

    [Fact]
    public void FixedNavigationRailKeepsAuxiliaryPanesSeparate()
    {
        var column = Load("MainWindow.xaml").Descendants(Ui + "ColumnDefinition").First();
        Assert.Equal("{x:Static local:MainPlanTheme.RailColumn}", (string?)column.Attribute("Width"));
        Assert.Null(column.Element(Ui + "ColumnDefinition.Style"));
        var parking = Load("MainWindow.xaml").Descendants(Ui + "StackPanel")
            .Single(e => (string?)e.Attribute(X + "Name") == "AuxiliaryParking");
        foreach (var name in new[] { "InventoryPane", "ProfilePane", "SettingsPane" })
            Assert.Single(parking.Elements(), e => (string?)e.Attribute(X + "Name") == name);
        var auxiliary = Load("AuxiliaryWindow.xaml");
        Assert.Equal("Manual", (string?)auxiliary.Root!.Attribute("WindowStartupLocation"));
        Assert.Equal("False", (string?)auxiliary.Root.Attribute("ShowInTaskbar"));
        Assert.Single(auxiliary.Descendants(Ui + "ContentControl"),
            e => (string?)e.Attribute(X + "Name") == "PaneHost");
    }
}
