using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace OrandOverlay;

public sealed partial class NormalCandidateView
{
    private readonly Dictionary<(string Goal, string Step), bool> _craftStepExpansion = new();
    private long _craftExpansionSession = -1;
    private const double CompactCraftBodySize = 12, CompactCraftMetaSize = 11, CompactCraftPortraitSize = 30;

    private static Grid CompactCraftColumns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(0.64, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static UIElement CompactCraftTableHeader()
    {
        // Match the inset used by the expander chevron without reserving any fixed-width unit columns.
        var grid = CompactCraftColumns(); grid.Margin = new(16, CraftTinyGap, 0, CraftTinyGap);
        var labels = new[] { "선택할 유닛", "키 · 횟수", "결과" };
        for (var i = 0; i < labels.Length; i++)
        {
            var label = Text(labels[i], CompactCraftMetaSize, OverlayTheme.PlanSecondary);
            label.TextAlignment = TextAlignment.Center; Grid.SetColumn(label, i); grid.Children.Add(label);
        }
        AutomationProperties.SetAutomationId(grid, "normal-craft-table-header");
        return grid;
    }

    private UIElement CompactCraftStep(NormalCraftStep step, int number)
    {
        var header = new StackPanel();
        header.Children.Add(CompactCraftAction(step));
        var projected = step.Ingredients.Any(i => i.PriorStepCount > 0);
        var status = !step.IsMaterialReady ? "재료 부족" : projected ? "앞 단계 필요" : "재료 준비됨";
        var color = !step.IsMaterialReady ? OverlayTheme.PlanWarning : projected ? Accent : OverlayTheme.PlanSuccess;
        var state = FreshnessText(number.ToString("00") + " · " + status,
            number.ToString("00") + " · 현재 재료 미확인", CompactCraftMetaSize, color, craft: true);
        state.Margin = new(0, CraftTinyGap, 0, 0); header.Children.Add(state);
        if (!step.Conditions.IsSatisfied || step.Ingredients.Any(i => i.IsResource))
        {
            var condition = Text("추가 조건·자원 확인 필요", CompactCraftMetaSize, OverlayTheme.PlanWarning);
            condition.ToolTip = CraftConditionGuidance(step);
            AutomationProperties.SetAutomationId(condition, "normal-craft-condition-" + step.UnitId);
            header.Children.Add(condition);
        }
        var materials = CompactCraftGrid(step.Ingredients.Any(i => i.UnitId == "PICK:A800"));
        AutomationProperties.SetAutomationId(materials, "normal-craft-materials-" + step.UnitId);
        foreach (var ingredient in step.Ingredients) materials.Children.Add(CompactCraftIngredient(ingredient, step.UnitId));
        var details = new StackPanel();
        AutomationProperties.SetName(details, step.Name + " 조합 재료와 확인할 조건");
        details.Children.Add(new Border { Child = materials,
            Padding = new(CraftTinyGap), CornerRadius = new(CraftTileRadius), Margin = new(16, CraftTinyGap, 0, CraftTinyGap) }.WithOverlayBackground(OverlayChrome.CanvasKey));
        if (!step.Conditions.IsSatisfied && !string.IsNullOrWhiteSpace(step.Conditions.Reason))
        {
            var conditionDetails = Text(CraftConditionGuidance(step), CompactCraftMetaSize, OverlayTheme.PlanSecondary);
            conditionDetails.Margin = new(16, 0, 0, CraftSmallGap); details.Children.Add(conditionDetails);
        }
        var expansionKey = (_model?.SelectedUnitId ?? "", step.UnitId);
        var expanded = _craftStepExpansion.GetValueOrDefault(expansionKey, number == 1);
        var expander = new Expander { Header = new CandidateHeader(step.Name + " 조합 재료", header), HeaderTemplate = CandidateHeaderTemplate, Content = details, IsExpanded = expanded,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Foreground = OverlayTheme.PlanSecondary,
            FontSize = CompactCraftBodySize, Padding = new(0) };
        AutomationProperties.SetAutomationId(expander, "normal-craft-step-toggle-" + step.UnitId);
        AutomationProperties.SetName(expander, step.Name + " 조합 재료");
        expander.Expanded += (_, _) => _craftStepExpansion[expansionKey] = true;
        expander.Collapsed += (_, _) => _craftStepExpansion[expansionKey] = false;
        var card = new Border { Child = expander, Padding = new(0, CraftTinyGap, 0, CraftTinyGap),
            BorderBrush = OverlayTheme.PlanLine, BorderThickness = new(0, 0, 0, 1) }.WithOverlayBackground(number == 1 ? OverlayChrome.WellKey : OverlayChrome.CanvasKey);
        AutomationProperties.SetAutomationId(card, "normal-craft-step-" + step.UnitId);
        AutomationProperties.SetName(card, number + ". " + step.Name + " ×" + step.OutputCount + " · " + step.CombineCount + "회 조합");
        return card;
    }

    private UIElement CompactCraftAction(NormalCraftStep step)
    {
        var route = CompactCraftColumns();
        var hasChat = step.CombineCommands.Count > 0;
        var conditionalSelection = step.UnitId == "rawcode:2C0h" && step.SelectionUnitId is null;
        var selection = hasChat ? CompactCraftTextUnit("채팅 입력", "선택 조건 미확인") :
            conditionalSelection ? CompactCraftTextUnit("사용할 유닛", "게임에서 확인") :
            CompactCraftUnit(step.SelectionUnitId, CraftSelectionName(step));
        if (conditionalSelection)
        {
            AutomationProperties.SetAutomationId(selection, "normal-craft-selection-KING:h0C2-PICK:A800");
            AutomationProperties.SetName(selection, CraftSelectionName(step));
            ((FrameworkElement)selection).ToolTip = "게임에서 사용할 유닛과 추가 조합 조건을 확인해 주세요";
        }
        route.Children.Add(selection);
        var command = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new(CraftTinyGap, 0, CraftTinyGap, 0) };
        command.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); command.ColumnDefinitions.Add(new());
        var key = Text(hasChat ? "채팅" : step.CombineKey ?? "확인", 16, OverlayTheme.PlanText, true);
        key.FontFamily = new FontFamily("Consolas, Malgun Gothic"); key.TextAlignment = TextAlignment.Center; key.Margin = new(0);
        var keycap = new Border { Child = key, MinWidth = 25, HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new(CraftTinyGap, 0, CraftTinyGap, 0),
            BorderBrush = OverlayTheme.PlanSecondary, BorderThickness = new(1, 1, 1, 3), CornerRadius = new(CraftTileRadius) }.WithOverlayBackground(OverlayChrome.RaisedKey);
        AutomationProperties.SetAutomationId(keycap, "normal-craft-key-" + step.UnitId); command.Children.Add(keycap);
        var count = Text(step.CombineCount + "회", CompactCraftMetaSize, OverlayTheme.PlanSecondary);
        count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new(CraftTinyGap, 0, 0, 0);
        Grid.SetColumn(count, 1); command.Children.Add(count);
        Grid.SetColumn(command, 1); route.Children.Add(command);
        var result = CompactCraftUnit(step.UnitId, step.Name, "×" + step.OutputCount);
        Grid.SetColumn(result, 2); route.Children.Add(result);
        var wrapper = new StackPanel(); wrapper.Children.Add(route);
        if (hasChat)
        {
            var chat = Text(step.CombineCommands[0], CompactCraftBodySize, OverlayTheme.PlanText, true);
            chat.ToolTip = string.Join(" / ", step.CombineCommands);
            wrapper.Children.Add(new Border { Child = chat,
                Padding = new(CraftSmallGap, CraftTinyGap, CraftSmallGap, CraftTinyGap), CornerRadius = new(CraftTileRadius) }.WithOverlayBackground(OverlayChrome.RaisedKey));
        }
        else if (step.CombineKey is null)
            wrapper.Children.Add(Text("조합 방법과 추가 조건은 게임에서 확인해 주세요", CompactCraftMetaSize, OverlayTheme.PlanWarning));
        AutomationProperties.SetAutomationId(wrapper, "normal-craft-action-" + step.UnitId);
        AutomationProperties.SetName(wrapper, hasChat
            ? "채팅: " + step.CombineCommands[0] + " · " + step.CombineCount + "회 · " + CraftSelectionName(step)
            : "선택: " + CraftSelectionName(step) + " · " + (step.CombineKey is null ? "조합 방법은 게임에서 확인" : "키: " + step.CombineKey) + " · " + step.CombineCount + "회");
        return wrapper;
    }

    private UIElement CompactCraftUnit(string? id, string name, string? count = null)
    {
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
        var unit = id is null ? null : _craftUnits.GetValueOrDefault(id);
        if (unit is not null)
        {
            var portrait = UnitImageFactory.Create(unit.Image, name, CompactCraftPortraitSize, id);
            portrait.Margin = new(0, 0, CraftSmallGap, 0); portrait.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(portrait, name); row.Children.Add(portrait);
        }
        var label = Text(name, CompactCraftBodySize, OverlayTheme.PlanText);
        label.Margin = new(0); label.VerticalAlignment = VerticalAlignment.Center;
        if (count is not null)
            label.Inlines.Add(new System.Windows.Documents.Run(" " + count)
                { FontSize = CompactCraftMetaSize, Foreground = OverlayTheme.PlanSecondary });
        Grid.SetColumn(label, 1); row.Children.Add(label); return row;
    }

    private static UIElement CompactCraftTextUnit(string title, string caption)
    {
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(Text(title, CompactCraftBodySize, OverlayTheme.PlanText));
        body.Children.Add(Text(caption, CompactCraftMetaSize, OverlayTheme.PlanWarning)); return body;
    }

    private static UniformGrid CompactCraftGrid(bool fullWidth = false)
    {
        var grid = new UniformGrid { Columns = fullWidth ? 1 : 2 };
        if (!fullWidth) grid.SizeChanged += (_, _) =>
        {
            var columns = Math.Max(1, (int)(grid.ActualWidth / 128));
            if (grid.Columns != columns) grid.Columns = columns;
        };
        return grid;
    }

    private FrameworkElement CompactCraftIngredient(NormalCraftIngredient ingredient, string stepId)
    {
        var row = new Grid { Margin = new(0, 1, CraftSmallGap, 1) };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
        var unit = _craftUnits.GetValueOrDefault(ingredient.UnitId);
        var name = CraftMaterialName(ingredient.UnitId, ingredient.Name);
        var portrait = UnitImageFactory.Create(unit?.Image ?? "", name, CraftMaterialIconSize, ingredient.UnitId);
        portrait.Margin = new(0, 0, CraftSmallGap, 0); portrait.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(portrait);
        var words = new StackPanel(); Grid.SetColumn(words, 1); row.Children.Add(words);
        words.Children.Add(Text(name + " ×" + ingredient.RequiredCount, CompactCraftBodySize, OverlayTheme.PlanText));
        var status = ingredient.IsResource ? "게임에서 확인" : "보유 " + ingredient.OwnedCount;
        if (!ingredient.IsResource && ingredient.PriorStepCount > 0) status += " · 앞 단계 " + ingredient.PriorStepCount;
        if (!ingredient.IsResource && ingredient.MissingCount > 0) status += " · " + ingredient.MissingCount + " 부족";
        var color = ingredient.IsResource || ingredient.MissingCount > 0 ? OverlayTheme.PlanWarning :
            ingredient.PriorStepCount > 0 ? Accent : OverlayTheme.PlanSuccess;
        words.Children.Add(FreshnessText(status, "이전: " + status, CompactCraftMetaSize, color, craft: true));
        var tile = new Border { Child = row };
        AutomationProperties.SetAutomationId(tile, "normal-craft-material-" + stepId + "/" + ingredient.UnitId);
        AutomationProperties.SetName(tile, name + " · 필요 " + ingredient.RequiredCount + " · 인식 " + ingredient.OwnedCount +
            " · 앞 단계 예상 " + ingredient.PriorStepCount + " · " + status);
        return tile;
    }
}
