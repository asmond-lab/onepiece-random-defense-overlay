using System.Xml.Linq;
using Xunit;

namespace OrandOverlay.Tests;

public sealed class StatsCompactBLayoutTests
{
    internal static string Source(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StatsOverlayWindow.xaml"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, file));
    }

    [Theory]
    [InlineData(OverlayDisplayMode.Full)]
    [InlineData(OverlayDisplayMode.StatsOnly)]
    public void CompactDimensionsMatchXamlAndScalingPolicy(OverlayDisplayMode mode)
    {
        var layout = OverlayLayoutPolicy.StatsLayout(mode);
        var doc = XDocument.Parse(Source("StatsOverlayWindow.xaml"));
        Assert.Equal(326, layout.Width);
        Assert.Equal(440, layout.Height);
        Assert.True(layout.NonCoreVisible);
        Assert.Equal(layout.Width, (double)doc.Root!.Attribute("Width")!);
        Assert.Equal(layout.Height, (double)doc.Root.Attribute("Height")!);
        var fhdShare = layout.Width * UiScale.FromScreen(1080, 1) / 1920;
        var qhdShare = layout.Width * UiScale.FromScreen(1440, 1) / 2560;
        Assert.Equal(fhdShare, qhdShare, 6);
        Assert.Contains("OverlayLayoutPolicy.StatsLayout(_mode).Width", Source("StatsOverlayWindow.xaml.cs"));
    }

    [Fact]
    public void ThreePrimaryColumnsAndExpandedContentShareBoundedVerticalScroll()
    {
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Parse(Source("StatsOverlayWindow.xaml"));
        XElement Named(string name) => Assert.Single(doc.Descendants().Where(e => (string?)e.Attribute(x + "Name") == name));
        var primary = Named("CoreKpiPanel");
        Assert.Equal("3", (string?)primary.Attribute("Columns"));
        Assert.Equal("1", (string?)primary.Attribute("Rows"));
        var frame = Named("MetricsFrame");
        Assert.Equal("{x:Static local:RandyPickTheme.Border}", (string?)frame.Attribute("BorderBrush"));
        Assert.Equal("{DynamicResource OverlayRaisedBrush}", (string?)frame.Attribute("Background"));
        Assert.Equal(frame, primary.Parent);
        Assert.Contains("randypick-logo-64.png", Source("StatsOverlayWindow.xaml"));
        Assert.Contains("{x:Static local:RandyPickBrand.ProductLabel}", Source("StatsOverlayWindow.xaml"));
        Assert.Equal("랜디픽 BETA " + OrandOverlay.UpdateService.CurrentVersion.ToString(3), OrandOverlay.RandyPickBrand.ProductLabel);
        var footer = Named("StatsFooterText").Parent!;
        Assert.Equal("0,1,0,0", (string?)footer.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource OverlayWellBrush}", (string?)footer.Attribute("Background"));
        var scroll = Named("StatsScroll");
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Contains(scroll, primary.Ancestors());
        Assert.Contains(scroll, Named("ConditionalStatsPanel").Ancestors());
        Assert.Equal("False", (string?)Named("ConditionalExpander").Attribute("IsExpanded"));
        var details = Named("StatsDetailsExpander");
        Assert.Equal("False", (string?)details.Attribute("IsExpanded"));
        Assert.DoesNotContain(details, primary.Ancestors());
        Assert.DoesNotContain(details, Named("CurrentStatsPanel").Ancestors());
        Assert.Contains(details, Named("ConditionalStatsPanel").Ancestors());
        Assert.Contains(details, Named("SourceDetailsText").Ancestors());
        Assert.Contains(details, Named("AdditionalRolesPanel").Ancestors());
        var rows = scroll.Parent!.Element(w + "Grid.RowDefinitions")!.Elements().ToArray();
        Assert.Equal(new[] { "Auto", "*", "Auto" }, rows.Select(r => (string)r.Attribute("Height")!).ToArray());
        Assert.Contains(doc.Descendants(w + "Border"), e => (string?)e.Attribute("Background") == "{DynamicResource OverlayShellBrush}");
        Assert.Contains("DragArea_OnMouseLeftButtonDown", Source("StatsOverlayWindow.xaml"));
        foreach (var id in new[] { "stats-observation", "stats-source", "stats-unknown", "stats-scroll", "stats-footer" })
            Assert.Contains(id, Source("StatsOverlayWindow.xaml"));
    }

    [Fact]
    public void RenderOrderAndEvidenceDoNotMixRoleDetailIntoTotals()
    {
        var source = Source("OverlayWindow.xaml.cs");
        var start = source.IndexOf("private void RenderCurrentStats", StringComparison.Ordinal);
        var end = source.IndexOf("private UIElement StatChip", start, StringComparison.Ordinal);
        var render = source[start..end];
        Assert.Contains("Stats.SetStatSource(stats)", render);
        Assert.Contains("Stats.ConditionalStatsPanel", render);
        Assert.Contains("StatsRoleChip", render);
        Assert.Contains("Stats.AdditionalRolesPanel.Children.Clear()", render);
        Assert.Contains("Stats.AdditionalStatsPanel", render);
        Assert.Contains("stats.TotalArmorReduction", render);
        Assert.Contains("stats.TotalSlow", render);
        Assert.Contains("stats.UnobservedStackingArmorReduction", source);
        Assert.Contains("stats.IsLegacyReferenceForSelectedMap", Source("StatsOverlayWindow.xaml.cs"));
        Assert.Contains("IsLegacyReferenceForSelectedMap", Source("StatsOverlayWindow.xaml.cs"));
    }
}
