using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private bool _missingMaterialsExpanded = true;
    private const double MissingPortraitSize = 32, MissingCountSize = 18;

    private void RenderMissingMaterials(NormalCraftPlan plan)
    {
        if (plan.GoalOwned) return;
        if (plan.MissingMaterials.Count > 0)
        {
            _hasMissingMaterials = true;
            Panel materials = _craftDetached
                ? CompactCraftGrid(plan.MissingMaterials.Any(m => m.UnitId == "PICK:A800")) : new StackPanel();
            AutomationProperties.SetAutomationId(materials, "normal-missing-grid");
            materials.Margin = new(0, CraftSmallGap, 0, 0);
            foreach (var item in plan.MissingMaterials.OrderBy(m => RecipeTreeBuilder.CraftTierOrder(m.Tier)).ThenBy(m => m.Name, StringComparer.CurrentCulture))
                materials.Children.Add(MissingMaterialChip(item));
            var header = "부족 재료 · " + plan.MissingMaterials.Count + "종 · " +
                plan.MissingMaterials.Sum(m => (decimal)m.MissingCount) + "개";
            var heading = new Grid(); heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new());
            AutomationProperties.SetName(heading, header);
            heading.Children.Add(FreshnessText("부족 재료", "마지막 부족 재료", _craftDetached ? CompactCraftBodySize : CraftBodySize, OverlayTheme.PlanText, true, craft: true));
            var total = Text(plan.MissingMaterials.Count + "종 · " + plan.MissingMaterials.Sum(m => (decimal)m.MissingCount) + "개", _craftDetached ? CompactCraftMetaSize : CraftMetaSize, OverlayTheme.PlanSecondary);
            total.TextAlignment = TextAlignment.Right; Grid.SetColumn(total, 1); heading.Children.Add(total);
            var section = new Expander { Header = new CandidateHeader(header, heading), HeaderTemplate = CandidateHeaderTemplate, Content = _craftDetached
                    ? new Border { Child = materials, Padding = new(CraftSmallGap), CornerRadius = new(CraftTileRadius) }.WithOverlayBackground(OverlayChrome.RaisedKey)
                    : materials,
                IsExpanded = _missingMaterialsExpanded, FontSize = CraftMetaSize,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Foreground = OverlayTheme.PlanSecondary, Margin = new(0, 0, 0, CraftSmallGap) };
            AutomationProperties.SetAutomationId(section, "normal-missing-materials");
            AutomationProperties.SetName(section, header);
            section.Expanded += (_, _) => _missingMaterialsExpanded = true;
            section.Collapsed += (_, _) => _missingMaterialsExpanded = false;
            _missingHost.Children.Add(section);
        }
        foreach (var resource in plan.ResourceRequirements.Where(r => r.Value > 0))
        {
            var name = resource.Key.ToUpperInvariant() switch
            {
                "GOLD" => "골드", "LUMBER" => "목재", "POINT" => "포인트", "RANDOM" => "랜덤 자원", _ => "기타 자원"
            };
            var line = Text(name + " " + resource.Value + " 필요 · 보유량 미확인", CraftMetaSize, OverlayTheme.PlanWarning);
            line.Margin = new(0, CraftTinyGap, 0, 0);
            AutomationProperties.SetAutomationId(line, "normal-missing-resource-" + resource.Key);
            if (_craftDetached) { _hasMissingMaterials = true; _missingHost.Children.Add(line); }
            else _craftResources.Children.Add(line);
        }
    }

    private FrameworkElement MissingMaterialChip(RecipeLeafProgress material)
    {
        if (_craftDetached) return CraftHudMissingMaterial(material);
        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new());
        var unit = _craftUnits.GetValueOrDefault(material.UnitId);
        var displayName = CraftMaterialName(material.UnitId, material.Name);
        var portrait = UnitImageFactory.Create(unit?.Image ?? material.Image, displayName, MissingPortraitSize, material.UnitId);
        portrait.Margin = new(0, 0, CraftSmallGap, 0); portrait.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetAutomationId(portrait, "normal-missing-portrait-" + material.UnitId);
        body.Children.Add(portrait);
        var words = new StackPanel(); Grid.SetColumn(words, 1); body.Children.Add(words);
        words.Children.Add(Text(displayName, CraftMetaSize, OverlayTheme.PlanText));
        var count = Text(material.MissingCount.ToString(), MissingCountSize, OverlayTheme.PlanWarning, true);
        count.FontFamily = new System.Windows.Media.FontFamily("Consolas, Malgun Gothic");
        count.Inlines.Add(new System.Windows.Documents.Run("개 부족")
            { FontSize = CraftMetaSize, FontWeight = FontWeights.Normal, Foreground = OverlayTheme.PlanSecondary });
        var badge = new Border { Child = count };
        AutomationProperties.SetAutomationId(badge, "normal-missing-count-" + material.UnitId);
        AutomationProperties.SetName(badge, material.MissingCount + "개 부족");
        words.Children.Add(badge);
        var label = displayName + " " + material.MissingCount + "개 부족";
        var slot = new Border { Child = body, Margin = new(0, 0, 0, CraftGap), ToolTip = label };
        AutomationProperties.SetAutomationId(slot, "normal-craft-material-missing/" + material.UnitId);
        AutomationProperties.SetName(slot, label);
        return slot;
    }

}
