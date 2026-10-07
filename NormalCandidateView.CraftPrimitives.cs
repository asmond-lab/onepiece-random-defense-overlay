using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private const double CraftHeadingSize = 16, CraftBodySize = 14, CraftMetaSize = 12, CraftKeySize = 22;
    private const double CraftGap = 6, CraftSmallGap = 5, CraftTinyGap = 3;
    private const double CraftRadius = 8, CraftTileRadius = 4;
    private const double CraftUnitIconSize = 36, CraftMaterialIconSize = 20, CraftGoalIconSize = 28;
    private const double CraftKeyMinWidth = 30;

    private UIElement CraftUnit(string? id, string name, string caption)
    {
        if (_craftDetached) return CraftHudUnit(id, name, caption);
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var label = Text(caption, CraftMetaSize, OverlayTheme.PlanSecondary); if (_compactCraft) label.Margin = new(0); label.TextAlignment = TextAlignment.Center;
        body.Children.Add(label);
        var unit = id is null ? null : _craftUnits.GetValueOrDefault(id);
        var portrait = UnitImageFactory.Create(unit?.Image ?? "", name, CraftUnitIconSize, id);
        portrait.Margin = new(0, CraftTinyGap, 0, CraftTinyGap); portrait.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(portrait, name); body.Children.Add(portrait);
        var text = Text(name, _compactCraft && !_craftDetached ? CraftMetaSize : CraftBodySize, OverlayTheme.PlanText, true); if (_compactCraft) text.Margin = new(0); text.TextAlignment = TextAlignment.Center;
        body.Children.Add(text); return body;
    }

    private static UIElement CraftTextUnit(string title, string caption)
    {
        var body = new StackPanel();
        body.Children.Add(Text(title, CraftBodySize, OverlayTheme.PlanText, true));
        body.Children.Add(Text(caption, CraftMetaSize, OverlayTheme.PlanWarning));
        return body;
    }

    private FrameworkElement CraftMaterialTile(string id, string name, long count, string status, Brush color, string? scope = null)
    {
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var unit = _craftUnits.GetValueOrDefault(id);
        var portrait = UnitImageFactory.Create(unit?.Image ?? "", name, CraftMaterialIconSize, id);
        portrait.Margin = new(0, CraftTinyGap, CraftSmallGap, 0); portrait.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(portrait);
        var words = new StackPanel(); Grid.SetColumn(words, 1); body.Children.Add(words);
        words.Children.Add(Text(name, CraftMetaSize, OverlayTheme.PlanText));
        var availability = FreshnessText(status, "이전: " + status, CraftMetaSize, color, craft: true);
        var quantity = Text("×" + count, CraftBodySize, OverlayTheme.PlanText, true);
        quantity.FontFamily = new FontFamily("Consolas");
        quantity.ToolTip = "필요 수량 " + count;
        if (_compactCraft)
        {
            var counts = new Grid(); counts.ColumnDefinitions.Add(new());
            counts.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            counts.Children.Add(availability); Grid.SetColumn(quantity, 1); counts.Children.Add(quantity);
            quantity.Margin = new(CraftTinyGap, 0, 0, 0); words.Children.Add(counts);
        }
        else
        {
            words.Children.Add(availability); quantity.Margin = new(CraftSmallGap, 0, 0, 0);
            Grid.SetColumn(quantity, 2); body.Children.Add(quantity);
        }
        var tile = new Border { Child = body, Width = _compactCraft ? _craftDetached ? 160 : 136 : double.NaN,
            Margin = _compactCraft ? new(0, 0, CraftTinyGap, 0) : new(0), Padding = new(0, CraftTinyGap, 0, CraftTinyGap),
            BorderBrush = OverlayTheme.PlanLine, BorderThickness = new(0, 0, 0, 1) };
        AutomationProperties.SetAutomationId(tile, "normal-craft-material-" + (scope is null ? "missing/" : scope + "/") + id);
        AutomationProperties.SetName(tile, name + " ×" + count + " · " + status);
        return tile;
    }
}
