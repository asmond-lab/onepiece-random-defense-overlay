using System.Xml.Linq;
using Xunit;
namespace OrandOverlay.Tests;

// Source/BAML contracts, not executed WPF evidence. The safe capture exercises UI separately.
public sealed class CoachDisclosureTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Source(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, file))) root = root.Parent;
        Assert.NotNull(root); return XDocument.Load(Path.Combine(root!.FullName, file));
    }
    [Fact]
    public void CompiledCoachBamlContainsDisclosureAndNoRepeatedActionLabel()
    {
        var assembly = typeof(BeginnerCoachView).Assembly;
        using var stream = assembly.GetManifestResourceStream("OrandOverlay.g.resources")!;
        using var resources = new System.Resources.ResourceReader(stream);
        string? coach = null;
        foreach (System.Collections.DictionaryEntry entry in resources)
        {
            if ((string)entry.Key != "beginnercoachview.baml") continue;
            using var bytes = new MemoryStream(); ((Stream)entry.Value!).CopyTo(bytes);
            coach = System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }
        Assert.NotNull(coach);
        var source = Source("BeginnerCoachView.xaml");
        var details = Assert.Single(source.Descendants(), e => (string?)e.Attribute(X + "Name") == "DetailsPanel");
        var preview = Assert.Single(source.Descendants(), e => (string?)e.Attribute(X + "Name") == "RecipePreviewLabel");
        Assert.Contains((string)details.Attribute("Header")!, coach);
        Assert.Contains((string)preview.Attribute("Text")!, coach);
        Assert.Single(source.Descendants(), e => (string?)e.Attribute(X + "Name") == "ActionText");
    }

    [Fact]
    public void DisclosureTemplateHasKeyboardFocusableHeaderAndVisibleFocus()
    {
        var doc = Source("App.xaml");
        var template = Assert.Single(doc.Descendants(Wpf + "ControlTemplate"), e => (string?)e.Attribute("TargetType") == "Expander");
        var toggle = Assert.Single(template.Descendants(Wpf + "ToggleButton"));
        Assert.Equal("True", (string?)toggle.Attribute("Focusable"));
        Assert.Contains(toggle.Descendants(Wpf + "Trigger"), e => (string?)e.Attribute("Property") == "IsKeyboardFocused");
    }

    [Fact]
    public void ReferencesAreCollapsedButActionAndBudgetAreOutsideDisclosure()
    {
        var doc = Source("BeginnerCoachView.xaml");
        XElement Named(string name) => Assert.Single(doc.Descendants(), e => (string?)e.Attribute(X + "Name") == name);
        var details = Named("DetailsPanel");
        Assert.DoesNotContain(doc.Descendants(), e => (string?)e.Attribute(X + "Name") is "GuideSourcePanel" or "GuideSourceText");
        Assert.Equal("False", (string?)details.Attribute("IsExpanded"));
        Assert.Equal("True", (string?)details.Attribute("IsTabStop"));
        foreach (var name in new[] { "OperationText", "UnknownText", "RecipePreviewText", "MilestoneText", "ReadinessSummary", "NavigationSummary", "QueenCondition" })
            Assert.Contains(details, Named(name).Ancestors());
        foreach (var name in new[] { "ActionText", "ControlsText", "CraftRecipeText", "ConstraintText", "ReasonText", "ConfirmationText", "AlternativeText", "PreserveText", "NavigationAlertText" })
            Assert.DoesNotContain(details, Named(name).Ancestors());
        Assert.Single(doc.Descendants(Wpf + "TextBlock"), e => (string?)e.Attribute("AutomationProperties.AutomationId") == "coach-action");
        Assert.DoesNotContain(details, Named("CraftCountPanel").Ancestors());
        Assert.Contains(details, Named("MaterialText").Ancestors());
    }
    [Fact]
    public void BrandLivesInFullWidthChromeAndRepeatedMainHeadingIsCollapsed()
    {
        var doc = Source("MainWindow.xaml");
        var texts = doc.Descendants(Wpf + "TextBlock").ToArray();
        var brand = Assert.Single(texts, e => (string?)e.Attribute(X + "Name") == "BrandText");
        var subtitle = Assert.Single(texts, e => (string?)e.Attribute(X + "Name") == "BrandSubtitle");
        var chrome = brand.Ancestors(Wpf + "Border").First();
        Assert.Equal("2", (string?)chrome.Attribute("Grid.ColumnSpan"));
        Assert.Contains(chrome, subtitle.Ancestors());
        Assert.Equal("18", (string?)brand.Attribute("FontSize"));
        var betaBadge = Assert.Single(texts, element =>
            (string?)element.Attribute("Text") == "{x:Static local:RandyPickBrand.BetaLabel}");
        Assert.Equal("{x:Static local:RandyPickBrand.ProductLabel}", (string?)doc.Root!.Attribute("Title"));
        Assert.Equal($"BETA {Source("OrandOverlay.csproj").Descendants("Version").Single().Value}", RandyPickBrand.BetaLabel);
        Assert.Equal($"BETA {UpdateService.CurrentVersion.ToString(3)}", RandyPickBrand.BetaLabel);
        Assert.Equal($"랜디픽 BETA {Source("OrandOverlay.csproj").Descendants("Version").Single().Value}", RandyPickBrand.ProductLabel);
        Assert.Contains(chrome, betaBadge.Ancestors());
        Assert.Equal("9", (string?)betaBadge.Attribute("FontSize"));
        Assert.NotEqual(brand.Parent, betaBadge.Parent);
        Assert.DoesNotContain(doc.Descendants(Wpf + "ColumnDefinition"), column =>
            (string?)column.Attribute("Width") == "{x:Static local:MainPlanTheme.RailColumn}");
        var planNav = Assert.Single(doc.Descendants(Wpf + "Button"), element =>
            (string?)element.Attribute(X + "Name") == "PlanNavigationButton");
        var horizontalNav = planNav.Ancestors(Wpf + "StackPanel").First();
        Assert.Equal("Horizontal", (string?)horizontalNav.Attribute("Orientation"));
        Assert.Equal(5, horizontalNav.Elements(Wpf + "Button").Count());
        var navRow = horizontalNav.Ancestors(Wpf + "Border").First();
        Assert.Equal("2", (string?)navRow.Attribute("Grid.ColumnSpan"));
        foreach (var name in new[] { "BoardHeadingText", "BoardSubheadingText" })
            Assert.Equal("Collapsed", (string?)Assert.Single(texts, e => (string?)e.Attribute(X + "Name") == name).Attribute("Visibility"));
    }
}
