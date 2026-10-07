using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private UIElement CraftHudUnit(string? id, string name, string caption)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
        var unit = id is null ? null : _craftUnits.GetValueOrDefault(id);
        var portrait = UnitImageFactory.Create(unit?.Image ?? "", name, CraftUnitIconSize, id);
        portrait.Margin = new(0, 0, CraftGap, 0); portrait.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(portrait, name); row.Children.Add(portrait);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Text(caption, CraftMetaSize, OverlayTheme.PlanSecondary));
        words.Children.Add(Text(name, CraftBodySize, OverlayTheme.PlanText, true));
        Grid.SetColumn(words, 1); row.Children.Add(words);
        return row;
    }

    private FrameworkElement CraftHudMissingMaterial(RecipeLeafProgress material)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
        var unit = _craftUnits.GetValueOrDefault(material.UnitId);
        var displayName = CraftMaterialName(material.UnitId, material.Name);
        var portrait = UnitImageFactory.Create(unit?.Image ?? material.Image, displayName, 34, material.UnitId);
        portrait.HorizontalAlignment = HorizontalAlignment.Left; portrait.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(portrait, "normal-missing-portrait-" + material.UnitId);
        var picture = new Grid { Width = 46, MinHeight = 40, Margin = new(0, 0, CraftSmallGap, 0), VerticalAlignment = VerticalAlignment.Top };
        picture.Children.Add(portrait);
        var value = Text("−" + material.MissingCount, CompactCraftBodySize, OverlayTheme.PlanCanvas, true);
        value.FontFamily = new FontFamily("Consolas, Malgun Gothic"); value.Margin = new(0);
        value.TextAlignment = TextAlignment.Center;
        var badge = new Border { Child = value, MinWidth = 23, MaxWidth = 46, Padding = new(2, 0, 2, 0),
            Background = OverlayTheme.PlanText, BorderBrush = OverlayTheme.PlanRaised, BorderThickness = new(2),
            CornerRadius = new(CraftTileRadius), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        AutomationProperties.SetAutomationId(badge, "normal-missing-count-" + material.UnitId);
        AutomationProperties.SetName(badge, material.MissingCount + "개 부족");
        picture.Children.Add(badge); row.Children.Add(picture);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Text(displayName, CompactCraftBodySize, OverlayTheme.PlanText); name.Margin = new(0); words.Children.Add(name);
        words.Children.Add(FreshnessText("보유 " + material.OwnedCount + " / 필요 " + material.RequiredCount,
            "이전 " + material.OwnedCount + " / 필요 " + material.RequiredCount, CompactCraftMetaSize, OverlayTheme.PlanSecondary, craft: true));
        Grid.SetColumn(words, 1); row.Children.Add(words);
        var label = displayName + " " + material.MissingCount + "개 부족 · 보유 " + material.OwnedCount + " / 필요 " + material.RequiredCount;
        var slot = new Border { Child = row, Margin = new(0, CraftTinyGap, CraftSmallGap, CraftSmallGap), ToolTip = label };
        AutomationProperties.SetAutomationId(slot, "normal-craft-material-missing/" + material.UnitId);
        AutomationProperties.SetName(slot, label);
        return slot;
    }
}
